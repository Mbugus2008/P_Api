using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParcelAPI.Data;
using ParcelAPI.Filters;
using ParcelAPI.Models;
using ParcelAPI.Services;

namespace ParcelAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KraController : ControllerBase
    {
        private readonly ILogger<KraController> _logger;
        private readonly ParcelContext _db;
        private readonly IEtimsService _etims;

        public KraController(ILogger<KraController> logger, ParcelContext db, IEtimsService etims)
        {
            _logger = logger;
            _db = db;
            _etims = etims;
        }

        private string ClientId => HttpContext.Items["ClientId"]?.ToString() ?? string.Empty;

        /// <summary>Get eTIMS settings for the current client</summary>
        [HttpGet("settings")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<EtimsSettings?>>> GetSettings()
        {
            try
            {
                var settings = await _db.Set<EtimsSettings>()
                    .FirstOrDefaultAsync(s => s.ClientCode == ClientId && s.IsActive);
                return Ok(new Results<EtimsSettings?> { Code = 0, Contents = settings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting KRA settings");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Save eTIMS settings for the current client</summary>
        [HttpPost("settings")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> SaveSettings([FromBody] EtimsSettingsRequest request)
        {
            try
            {
                var existing = await _db.Set<EtimsSettings>()
                    .FirstOrDefaultAsync(s => s.ClientCode == ClientId);

                if (existing != null)
                {
                    existing.TinPin = request.TinPin;
                    existing.BranchId = request.BranchId ?? "00";
                    existing.DeviceSerialNo = request.DeviceSerialNo ?? ClientId;
                    existing.ApiUsername = request.ApiUsername;
                    existing.ApiPassword = request.ApiPassword;
                    existing.Environment = request.Environment ?? "Sandbox";
                    existing.IsActive = true;
                }
                else
                {
                    _db.Set<EtimsSettings>().Add(new EtimsSettings
                    {
                        ClientCode = ClientId,
                        TinPin = request.TinPin,
                        BranchId = request.BranchId ?? "00",
                        DeviceSerialNo = request.DeviceSerialNo ?? ClientId,
                        ApiUsername = request.ApiUsername,
                        ApiPassword = request.ApiPassword,
                        Environment = request.Environment ?? "Sandbox",
                        IsActive = true
                    });
                }

                await _db.SaveChangesAsync();
                return Ok(new Results<object> { Code = 0, Desc = "Settings saved" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving KRA settings");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Send an invoice to KRA eTIMS</summary>
        [HttpPost("send-invoice")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<EtimsResult>>> SendInvoice([FromBody] EtimsInvoiceRequest request)
        {
            try
            {
                var settings = await _db.Set<EtimsSettings>()
                    .FirstOrDefaultAsync(s => s.ClientCode == ClientId && s.IsActive);

                if (settings == null)
                    return BadRequest(new Results<EtimsResult>
                    {
                        Code = -1,
                        Desc = "No eTIMS settings found for this client. Configure KRA settings first."
                    });

                if (string.IsNullOrEmpty(settings.ApiUsername) || string.IsNullOrEmpty(settings.ApiPassword))
                    return BadRequest(new Results<EtimsResult>
                    {
                        Code = -1,
                        Desc = "KRA API credentials not configured. Save settings first."
                    });

                var invoice = new EtimsInvoice
                {
                    InvoiceNo = request.DocumentNo ?? "INV-" + DateTime.UtcNow.Ticks,
                    Description = request.Description ?? "Parcel Delivery Service",
                    Amount = request.Amount,
                    TaxCode = request.TaxCode ?? "E"
                };

                var result = await _etims.SendInvoiceAsync(settings, invoice);
                await _db.SaveChangesAsync(); // persist cmcKey and invoice counter

                return Ok(new Results<EtimsResult>
                {
                    Code = result.IsSuccess ? 0 : -1,
                    Desc = result.Message,
                    Contents = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending KRA invoice");
                return StatusCode(500, Err<EtimsResult>(ex.Message));
            }
        }

        private static Results<T> Err<T>(string msg) => new() { Code = -1, Desc = msg };
        private static Results<object> Err(string msg) => new() { Code = -1, Desc = msg };
    }

    public class EtimsSettingsRequest
    {
        public string TinPin { get; set; } = string.Empty;
        public string? BranchId { get; set; }
        public string? DeviceSerialNo { get; set; }
        public string ApiUsername { get; set; } = string.Empty;
        public string ApiPassword { get; set; } = string.Empty;
        public string? Environment { get; set; }
    }

    public class EtimsInvoiceRequest
    {
        public string? DocumentNo { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public string? TaxCode { get; set; }
    }
}
