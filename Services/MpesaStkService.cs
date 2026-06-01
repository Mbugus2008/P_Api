using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParcelAPI.Data;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public class MpesaStkService : IMpesaStkService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly Client? _clientInfo;
        private readonly ParcelContext _dbContext;

        public MpesaStkService(HttpClient httpClient, Client? clientInfo, ILogger logger, ParcelContext dbContext)
        {
            _httpClient = httpClient;
            _clientInfo = clientInfo;
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task<MpesaStkResponse> InitiateStkPushAsync(MpesaStkRequest request, string callbackUrl)
        {
            using var navConfigService = new NavMpesaConfigService(_clientInfo, _logger);
            var navConfig = await navConfigService.ReadConfigAsync();

            if (navConfig == null)
                throw new InvalidOperationException("M-Pesa configuration not found in NAV.");

            if (string.IsNullOrWhiteSpace(navConfig.Consumer_Key) || string.IsNullOrWhiteSpace(navConfig.Consumer_Secret))
                throw new InvalidOperationException("Consumer Key or Consumer Secret not configured.");

            var token = await GetAccessTokenAsync(navConfig);
            var baseUrl = NormalizeBaseUrl(navConfig.Environment == MpesaCon.Environment.Production
                ? navConfig.Production_URL
                : navConfig.Sandbox_URL);

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = navConfig.Environment == MpesaCon.Environment.Production
                    ? "https://api.safaricom.co.ke"
                    : "https://sandbox.safaricom.co.ke";

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var rawPassword = $"{navConfig.Short_Code}{navConfig.Passkey}{timestamp}";
            var password = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPassword));

            var phone = FormatPhoneNumber(request.PhoneNumber);
            var amount = (int)request.Amount;

            var payload = new
            {
                BusinessShortCode = navConfig.Short_Code,
                Password = password,
                Timestamp = timestamp,
                TransactionType = "CustomerPayBillOnline",
                Amount = amount,
                PartyA = phone,
                PartyB = navConfig.Short_Code,
                PhoneNumber = phone,
                CallBackURL = callbackUrl,
                AccountReference = request.Reference ?? "ParcelPayment",
                TransactionDesc = request.Description ?? "Parcel delivery payment"
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            _logger.LogInformation("Initiating STK push: Phone={Phone}, Amount={Amount}, Ref={Reference}",
                phone, amount, request.Reference);

            var response = await _httpClient.PostAsync($"{baseUrl}/mpesa/stkpush/v1/processrequest", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("STK push response: {Status} - {Body}", response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"STK push failed: {response.StatusCode} - {responseBody}");
            }

            var result = JsonSerializer.Deserialize<MpesaStkResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
                throw new InvalidOperationException("Failed to parse STK push response");

            // Persist pending status
            if (!string.IsNullOrEmpty(result.CheckoutRequestId))
            {
                var status = new MpesaStkStatus
                {
                    CheckoutRequestId = result.CheckoutRequestId,
                    MerchantRequestId = result.MerchantRequestId,
                    ResultCode = -1,
                    ResultDescription = result.CustomerMessage ?? "Pending - waiting for customer",
                    Amount = request.Amount,
                    PhoneNumber = phone,
                    Reference = request.Reference,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.MpesaStkStatuses.Add(status);
                await _dbContext.SaveChangesAsync();
            }

            return result;
        }

        public async Task HandleCallbackAsync(MpesaStkCallback callback)
        {
            var stk = callback.Body?.StkCallback;
            if (stk == null)
            {
                _logger.LogWarning("Received empty STK callback");
                return;
            }

            _logger.LogInformation("STK callback received: Checkout={Checkout}, Result={ResultCode}, Desc={Desc}",
                stk.CheckoutRequestId, stk.ResultCode, stk.ResultDesc);

            if (string.IsNullOrEmpty(stk.CheckoutRequestId))
            {
                _logger.LogWarning("STK callback missing CheckoutRequestID");
                return;
            }

            var existing = await _dbContext.MpesaStkStatuses
                .FirstOrDefaultAsync(s => s.CheckoutRequestId == stk.CheckoutRequestId);

            if (existing == null)
            {
                _logger.LogWarning("STK callback for unknown CheckoutRequestID: {Checkout}", stk.CheckoutRequestId);
                return;
            }

            existing.ResultCode = stk.ResultCode;
            existing.ResultDescription = stk.ResultDesc;
            existing.ProcessedAt = DateTime.UtcNow;
            existing.Status = stk.ResultCode switch
            {
                0 => "Success",
                1032 => "Cancelled",
                1037 => "Timeout",
                _ => "Failed"
            };

            if (stk.CallbackMetadata?.Item != null)
            {
                foreach (var item in stk.CallbackMetadata.Item)
                {
                    if (item.Name == "Amount" && item.Value != null)
                        existing.Amount = Convert.ToDecimal(item.Value, CultureInfo.InvariantCulture);
                    else if (item.Name == "MpesaReceiptNumber" && item.Value != null)
                        existing.MpesaReceiptNumber = item.Value.ToString();
                    else if (item.Name == "PhoneNumber" && item.Value != null)
                        existing.PhoneNumber = item.Value.ToString();
                    else if (item.Name == "TransactionDate" && item.Value != null)
                    {
                        if (long.TryParse(item.Value.ToString(), out var txDate))
                            existing.TransactionDate = ParseSafaricomDate(txDate);
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("STK status saved to database: {Checkout} = {Status}",
                stk.CheckoutRequestId, existing.Status);
        }

        public async Task<MpesaStkStatus?> GetStatusAsync(string checkoutRequestId)
        {
            var existing = await _dbContext.MpesaStkStatuses
                .FirstOrDefaultAsync(s => s.CheckoutRequestId == checkoutRequestId);

            if (existing == null)
                return null;

            // If the callback has already resolved this transaction, return as-is.
            if (existing.Status != "Pending")
                return existing;

            // The Safaricom callback may never reach us (firewall / port not
            // publicly reachable). Give it a short head start, then actively
            // query Safaricom's STK Push Query API as a fallback so the payment
            // still gets picked up.
            if ((DateTime.UtcNow - existing.CreatedAt) < TimeSpan.FromSeconds(8))
                return existing;

            try
            {
                await QueryAndUpdateFromSafaricomAsync(existing);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "STK push query fallback failed for {Checkout}", checkoutRequestId);
            }

            return existing;
        }

        private async Task QueryAndUpdateFromSafaricomAsync(MpesaStkStatus existing)
        {
            using var navConfigService = new NavMpesaConfigService(_clientInfo, _logger);
            var navConfig = await navConfigService.ReadConfigAsync();

            if (navConfig == null ||
                string.IsNullOrWhiteSpace(navConfig.Consumer_Key) ||
                string.IsNullOrWhiteSpace(navConfig.Consumer_Secret))
            {
                return;
            }

            var token = await GetAccessTokenAsync(navConfig);
            var baseUrl = NormalizeBaseUrl(navConfig.Environment == MpesaCon.Environment.Production
                ? navConfig.Production_URL
                : navConfig.Sandbox_URL);

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = navConfig.Environment == MpesaCon.Environment.Production
                    ? "https://api.safaricom.co.ke"
                    : "https://sandbox.safaricom.co.ke";

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var rawPassword = $"{navConfig.Short_Code}{navConfig.Passkey}{timestamp}";
            var password = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPassword));

            var payload = new
            {
                BusinessShortCode = navConfig.Short_Code,
                Password = password,
                Timestamp = timestamp,
                CheckoutRequestID = existing.CheckoutRequestId
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.PostAsync($"{baseUrl}/mpesa/stkpushquery/v1/query", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("STK query response for {Checkout}: {Status} - {Body}",
                existing.CheckoutRequestId, response.StatusCode, responseBody);

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // While the customer hasn't acted yet, Safaricom returns an error
            // envelope (e.g. errorCode 500.001.1001 "transaction is being
            // processed"). In that case there is no ResultCode to act on.
            if (!root.TryGetProperty("ResultCode", out var resultCodeProp))
                return;

            var resultCodeStr = resultCodeProp.ValueKind == JsonValueKind.String
                ? resultCodeProp.GetString()
                : resultCodeProp.GetRawText();

            if (!int.TryParse(resultCodeStr, out var resultCode))
                return;

            // Still pending on Safaricom's side - leave the record untouched.
            if (resultCode == -1)
                return;

            var resultDesc = root.TryGetProperty("ResultDesc", out var descProp)
                ? descProp.GetString()
                : null;

            existing.ResultCode = resultCode;
            existing.ResultDescription = resultDesc ?? existing.ResultDescription;
            existing.ProcessedAt = DateTime.UtcNow;
            existing.Status = resultCode switch
            {
                0 => "Success",
                1032 => "Cancelled",
                1037 => "Timeout",
                _ => "Failed"
            };

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("STK status updated via query fallback: {Checkout} = {Status}",
                existing.CheckoutRequestId, existing.Status);
        }

        private static DateTime? ParseSafaricomDate(long value)
        {
            if (value.ToString().Length == 14 &&
                DateTime.TryParseExact(value.ToString(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }
            return null;
        }

        private static string FormatPhoneNumber(string phone)
        {
            var cleaned = phone.Trim().Replace(" ", "").Replace("-", "");
            if (cleaned.StartsWith("0"))
                return "254" + cleaned.Substring(1);
            if (cleaned.StartsWith("+"))
                return cleaned.Substring(1);
            if (!cleaned.StartsWith("254"))
                return "254" + cleaned;
            return cleaned;
        }

        private static string NormalizeBaseUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}".TrimEnd('/');
            }
            return url.Trim().TrimEnd('/');
        }

        private async Task<string> GetAccessTokenAsync(MpesaCon.MpesaConfig navConfig)
        {
            var baseUrl = NormalizeBaseUrl(navConfig.Environment == MpesaCon.Environment.Production
                ? navConfig.Production_URL
                : navConfig.Sandbox_URL);

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = navConfig.Environment == MpesaCon.Environment.Production
                    ? "https://api.safaricom.co.ke"
                    : "https://sandbox.safaricom.co.ke";

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{navConfig.Consumer_Key}:{navConfig.Consumer_Secret}"));

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.GetAsync($"{baseUrl}/oauth/v1/generate?grant_type=client_credentials");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OAuth failed: {response.StatusCode} - {responseBody}");
            }

            var tokenResponse = JsonSerializer.Deserialize<DarajaTokenResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return tokenResponse?.AccessToken ?? throw new InvalidOperationException("Access token was null");
        }
    }
}
