using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public class MpesaQrService : IMpesaQrService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly Client _clientInfo;
        private string? _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public MpesaQrService(HttpClient httpClient, Client clientInfo, ILogger logger)
        {
            _httpClient = httpClient;
            _clientInfo = clientInfo;
            _logger = logger;
        }

        private static string NormalizeBaseUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;
            // Users sometimes paste full endpoint URLs; extract just the scheme+host
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}".TrimEnd('/');
            }
            return url.Trim().TrimEnd('/');
        }

        public async Task<MpesaQrResponse> GenerateQrCodeAsync(MpesaQrRequest request)
        {
            // Read M-Pesa config from NAV
            using var navConfigService = new NavMpesaConfigService(_clientInfo, _logger);
            var navConfig = await navConfigService.ReadConfigAsync();

            if (navConfig == null)
                throw new InvalidOperationException("M-Pesa configuration not found in NAV. Please set up Table 50080.");

            if (string.IsNullOrWhiteSpace(navConfig.Consumer_Key) || string.IsNullOrWhiteSpace(navConfig.Consumer_Secret))
                throw new InvalidOperationException("Consumer Key or Consumer Secret not configured in NAV.");

            var token = await GetAccessTokenAsync(navConfig);
            var baseUrl = NormalizeBaseUrl(navConfig.Environment == MpesaCon.Environment.Production
                ? navConfig.Production_URL
                : navConfig.Sandbox_URL);

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = navConfig.Environment == MpesaCon.Environment.Production
                    ? "https://api.safaricom.co.ke"
                    : "https://sandbox.safaricom.co.ke";

            var trxCode = navConfig.Transaction_Code.ToString();
            var payload = new
            {
                MerchantName = navConfig.Merchant_Name,
                RefNo = request.Reference ?? Guid.NewGuid().ToString("N")[..8],
                Amount = (int)request.Amount,
                TrxCode = trxCode,
                CPI = navConfig.Short_Code,
                Size = request.Size ?? "300"
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            _logger.LogInformation("Safaricom QR request to {Url}: {Payload}",
                $"{baseUrl}/mpesa/qrcode/v1/generate", payloadJson);

            var content = new StringContent(
                payloadJson,
                Encoding.UTF8,
                "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.PostAsync($"{baseUrl}/mpesa/qrcode/v1/generate", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Safaricom QR API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Safaricom API error: {response.StatusCode} - {responseBody}");
            }

            var result = JsonSerializer.Deserialize<MpesaQrResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation(
                "Safaricom QR response: ResponseCode={Code}, ResponseDescription={Desc}, QRCodeLength={Len}",
                result?.ResponseCode, result?.ResponseDescription, result?.QrCodeBase64?.Length ?? 0);

            return result ?? throw new InvalidOperationException("Failed to parse QR API response");
        }

        private async Task<string> GetAccessTokenAsync(MpesaCon.MpesaConfig navConfig)
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                return _cachedToken;
            }

            var baseUrl = NormalizeBaseUrl(navConfig.Environment == MpesaCon.Environment.Production
                ? navConfig.Production_URL
                : navConfig.Sandbox_URL);

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = navConfig.Environment == MpesaCon.Environment.Production
                    ? "https://api.safaricom.co.ke"
                    : "https://sandbox.safaricom.co.ke";

            _logger.LogInformation("Safaricom OAuth URL: {Url}, Env: {Env}", baseUrl + "/oauth/v1/generate", navConfig.Environment);

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{navConfig.Consumer_Key}:{navConfig.Consumer_Secret}"));

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.GetAsync($"{baseUrl}/oauth/v1/generate?grant_type=client_credentials");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Safaricom OAuth error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"OAuth failed: {response.StatusCode} - {responseBody}");
            }

            var tokenResponse = JsonSerializer.Deserialize<DarajaTokenResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _cachedToken = tokenResponse?.AccessToken;
            if (int.TryParse(tokenResponse?.ExpiresIn, out var expiresIn))
            {
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
            }
            else
            {
                _tokenExpiry = DateTime.UtcNow.AddMinutes(50);
            }

            return _cachedToken ?? throw new InvalidOperationException("Access token was null");
        }
    }
}
