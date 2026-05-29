using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ParcelAPI.Clients;
using ParcelAPI.Data;
using ParcelAPI.Filters;
using ParcelAPI.Models;
using ParcelAPI.Services;

namespace ParcelAPI.Controllers
{
    [Route("api/Parcel/mpesa")]
    [ApiController]
    [ServiceFilter(typeof(ClientIdentifierFilter))]
    public class MpesaController : ControllerBase
    {
        private readonly ILogger<MpesaController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ParcelContext _dbContext;
        private readonly IConfiguration _configuration;

        public MpesaController(
            ILogger<MpesaController> logger,
            IHttpClientFactory httpClientFactory,
            ParcelContext dbContext,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _dbContext = dbContext;
            _configuration = configuration;
        }

        private Client ClientInfo => ((IClient)HttpContext.Items["Client"]!).ClientInfo;

        private string GetCallbackUrl()
        {
            var baseUrl = _configuration["Mpesa:CallbackBaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                return baseUrl.TrimEnd('/') + "/api/Parcel/mpesa/callback";
            }
            return $"{Request.Scheme}://{Request.Host}/api/Parcel/mpesa/callback";
        }

        [HttpGet("download")]
        public IActionResult DownloadApk()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "downloads", "ParcelApp.apk");
            if (!System.IO.File.Exists(path))
            {
                return NotFound(new Results<object> { Code = -1, Desc = "APK not found" });
            }
            return PhysicalFile(path, "application/vnd.android.package-archive", "ParcelApp.apk");
        }

        [HttpPost("qrcode")]
        public async Task<ActionResult<Results<MpesaQrResponse>>> GenerateQrCode([FromBody] MpesaQrRequest request)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var service = new MpesaQrService(httpClient, ClientInfo, _logger);
                var result = await service.GenerateQrCodeAsync(request);

                return Ok(new Results<MpesaQrResponse>
                {
                    Code = 0,
                    Desc = result.ResponseDescription,
                    Contents = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating M-Pesa QR code");
                return StatusCode(500, new Results<MpesaQrResponse>
                {
                    Code = -1,
                    Desc = ex.Message
                });
            }
        }

        [HttpPost("stkpush")]
        public async Task<ActionResult<Results<MpesaStkResponse>>> InitiateStkPush([FromBody] MpesaStkRequest request)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var service = new MpesaStkService(httpClient, ClientInfo, _logger, _dbContext);
                var result = await service.InitiateStkPushAsync(request, GetCallbackUrl());

                return Ok(new Results<MpesaStkResponse>
                {
                    Code = 0,
                    Desc = result.CustomerMessage ?? "STK push initiated",
                    Contents = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating STK push");
                return StatusCode(500, new Results<MpesaStkResponse>
                {
                    Code = -1,
                    Desc = ex.Message
                });
            }
        }

        [HttpPost("callback")]
        public async Task<IActionResult> StkCallback([FromBody] MpesaStkCallback callback)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var service = new MpesaStkService(httpClient, null, _logger, _dbContext);
                await service.HandleCallbackAsync(callback);
                return Ok(new { Result = "Success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling STK callback");
                return StatusCode(500, new { Result = "Error", Message = ex.Message });
            }
        }

        /// <summary>
        /// Query the existing [MPESA Transactions] table by BillRefNumber
        /// (the reference used when generating the QR code).
        /// </summary>
        [HttpGet("c2b/{reference}")]
        public async Task<IActionResult> GetC2BTransaction(string reference)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    return BadRequest(new Results<object> { Code = -1, Desc = "Reference is required" });
                }

                var conn = _dbContext.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                    await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT TOP 1 [Receipt No_], [Paid In], [Completion Time],
                                 [Detaills], [Phone], [Transaction Date]
                    FROM [MPESA Transactions]
                    WHERE [Detaills] = @Reference OR [Comments] = @Reference
                    ORDER BY [Completion Time] DESC";
                cmd.Parameters.Add(new SqlParameter("@Reference", reference));

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Ok(new Results<object>
                    {
                        Code = -1,
                        Desc = "Transaction not found"
                    });
                }

                return Ok(new Results<object>
                {
                    Code = 0,
                    Desc = "Transaction found",
                    Contents = new
                    {
                        TransID = reader["Receipt No_"].ToString(),
                        TransAmount = reader.GetDecimal(reader.GetOrdinal("Paid In")),
                        TransTime = reader["Completion Time"].ToString(),
                        BillRefNumber = reader["Detaills"].ToString(),
                        MSISDN = reader["Phone"].ToString(),
                        TransactionDate = reader["Transaction Date"].ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying [MPESA Transactions] for reference {Reference}", reference);
                return StatusCode(500, new Results<object> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpGet("stkpush/status/{checkoutRequestId}")]
        public async Task<IActionResult> GetStkStatus(string checkoutRequestId)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var service = new MpesaStkService(httpClient, ClientInfo, _logger, _dbContext);
                var status = await service.GetStatusAsync(checkoutRequestId);

                if (status == null)
                {
                    return Ok(new Results<object>
                    {
                        Code = -1,
                        Desc = "Status not found"
                    });
                }

                return Ok(new Results<MpesaStkStatus>
                {
                    Code = status.ResultCode == 0 ? 0 : -1,
                    Desc = status.ResultDescription,
                    Contents = status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting STK status");
                return StatusCode(500, new Results<object>
                {
                    Code = -1,
                    Desc = ex.Message
                });
            }
        }
    }
}
