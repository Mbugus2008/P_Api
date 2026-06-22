using ParcelAPI.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ParcelAPI.Services
{
    public interface IEtimsService
    {
        Task<EtimsResult> SendInvoiceAsync(EtimsSettings settings, EtimsInvoice invoice);
    }

    public class EtimsService : IEtimsService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EtimsService> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public EtimsService(IHttpClientFactory httpClientFactory, ILogger<EtimsService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<EtimsResult> SendInvoiceAsync(EtimsSettings s, EtimsInvoice invoice)
        {
            try
            {
                var baseUrl = s.Environment == "Production"
                    ? "https://etims-api.kra.go.ke/etims-api"
                    : "https://sbx.kra.go.ke";

                var token = await AuthenticateAsync(baseUrl, s.ApiUsername, s.ApiPassword);
                if (string.IsNullOrEmpty(token))
                    return EtimsResult.Fail("Authentication failed — check API credentials");

                await InitializeDeviceAsync(baseUrl, token, s.TinPin, s.BranchId, s.DeviceSerialNo, s);

                var result = await PostInvoiceAsync(baseUrl, token, s, invoice);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "eTIMS send invoice failed");
                return EtimsResult.Fail(ex.Message);
            }
        }

        private async Task<string> AuthenticateAsync(string baseUrl, string username, string password)
        {
            var client = _httpClientFactory.CreateClient();
            var authBytes = Encoding.UTF8.GetBytes($"{username}:{password}");
            var authHeader = Convert.ToBase64String(authBytes);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/oauth2/v1/generate")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Basic", authHeader) }
            };
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("KRA auth failed: {StatusCode} {Body}", (int)response.StatusCode, body);
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("access_token", out var tok) ? tok.GetString() ?? string.Empty : string.Empty;
        }

        private async Task InitializeDeviceAsync(string baseUrl, string token, string tin, string bhfId, string dvcSrlNo, EtimsSettings settings)
        {
            var client = _httpClientFactory.CreateClient();
            var payload = JsonSerializer.Serialize(new { tin, bhfId, dvcSrlNo }, JsonOpts);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/etims-oscu/v1/selectInitOsdcInfo")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("cmcKey", out var cmc) && !string.IsNullOrEmpty(cmc.GetString()))
                    {
                        settings.CmcKey = cmc.GetString();
                        _logger.LogInformation("KRA cmcKey obtained: {CmcKey}", settings.CmcKey);
                    }
                }
                catch { /* best effort */ }
            }
            else
            {
                _logger.LogWarning("KRA init device returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            }
        }

        private async Task<EtimsResult> PostInvoiceAsync(string baseUrl, string token, EtimsSettings s, EtimsInvoice invoice)
        {
            var client = _httpClientFactory.CreateClient();

            // KRA requires sequential integer invoice numbers
            var nextInvNo = (s.LastInvoiceNo ?? 0) + 1;
            var invNo = nextInvNo.ToString();

            var payload = new
            {
                tin = s.TinPin,
                bhfId = s.BranchId,
                invcNo = invNo,
                salesTrnsItems = new[]
                {
                    new
                    {
                        itemCd = "PARCEL",
                        itemNm = invoice.Description ?? "Parcel Delivery",
                        qty = 1,
                        prc = invoice.Amount,
                        splyAmt = invoice.Amount,
                        dcRt = 0,
                        dcAmt = 0,
                        taxTyCd = invoice.TaxCode ?? "E",
                        taxAmt = 0
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload, JsonOpts);
            _logger.LogInformation("KRA POST Invoice: {Payload}", json);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/etims-oscu/v1/sendSalesTrns")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Pass cmcKey and bhfId as headers per KRA OSCU spec
            if (!string.IsNullOrEmpty(s.CmcKey))
                request.Headers.Add("cmcKey", s.CmcKey);
            request.Headers.Add("tin", s.TinPin);
            request.Headers.Add("bhfId", s.BranchId);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("KRA Response [{Code}]: {Body}", (int)response.StatusCode, body);

            if (!response.IsSuccessStatusCode)
                return EtimsResult.Fail($"KRA returned {(int)response.StatusCode}: {Truncate(body, 200)}");

            // Success — increment the invoice counter
            s.LastInvoiceNo = nextInvNo;

            using var doc = JsonDocument.Parse(body);

            var resultMsg = "Invoice submitted";
            if (doc.RootElement.TryGetProperty("resultMsg", out var rmsg))
                resultMsg = rmsg.GetString() ?? resultMsg;

            var kraInvNo = "";
            var qrCode = "";

            if (doc.RootElement.TryGetProperty("intrlData", out var intrl))
            {
                if (intrl.TryGetProperty("invcNo", out var invc)) kraInvNo = invc.GetString() ?? "";
                if (intrl.TryGetProperty("sdcId", out var sdc)) qrCode = sdc.GetString() ?? "";
            }

            return EtimsResult.Ok(kraInvNo, qrCode, resultMsg);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    }

    public class EtimsInvoice
    {
        public string InvoiceNo { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public string? TaxCode { get; set; } = "E"; // E=Exempt, V=VAT, Z=Zero-rated
    }

    public class EtimsResult
    {
        public bool IsSuccess { get; set; }
        public string? Message { get; set; }
        public string? KRAInvoiceNo { get; set; }
        public string? QrCodeData { get; set; }

        public static EtimsResult Fail(string msg) => new() { IsSuccess = false, Message = msg };
        public static EtimsResult Ok(string invNo, string qr, string msg) => new()
        {
            IsSuccess = true,
            KRAInvoiceNo = invNo,
            QrCodeData = qr,
            Message = msg
        };
    }
}
