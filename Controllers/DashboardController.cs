using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParcelAPI.Clients;
using ParcelAPI.Data;
using ParcelAPI.Filters;
using ParcelAPI.Models;

namespace ParcelAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly ILogger<DashboardController> _logger;
        private readonly ParcelContext _db;

        public DashboardController(ILogger<DashboardController> logger, ParcelContext db)
        {
            _logger = logger;
            _db = db;
        }

        /// <summary>List all active clients (no auth required)</summary>
        [HttpGet("clients")]
        public async Task<ActionResult<Results<object[]>>> GetClients()
        {
            try
            {
                var clients = await _db.Clients
                    .Where(c => c.Active)
                    .OrderBy(c => c.ClientName)
                    .Select(c => new { c.ClientCode, c.ClientName })
                    .ToArrayAsync();

                return Ok(new Results<object[]> { Code = 0, Contents = clients });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading clients");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Summary stats</summary>
        [HttpGet("summary")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetSummary()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();
                var batches = client.NavBatchService != null
                    ? (await client.NavBatchService.ReadMultipleBatchesAsync(null, 0) ?? Array.Empty<P_Batches.ParcelBatches>())
                    : Array.Empty<P_Batches.ParcelBatches>();

                return Ok(new Results<object>
                {
                    Code = 0,
                    Contents = new
                    {
                        totalParcels = parcels.Length,
                        totalRevenue = parcels.Sum(p => p.Amount_Paid),
                        activeBatches = batches.Count(b =>
                            b.Status == P_Batches.Status.Pending || b.Status == P_Batches.Status.Intransit),
                        collected = parcels.Count(p => p.Status == Parcels.Status.Collected)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting summary");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Parcels grouped by status</summary>
        [HttpGet("parcels-by-status")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetParcelsByStatus()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var grouped = parcels
                    .GroupBy(p => p.Status.ToString())
                    .Select(g => new { status = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parcels by status");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Parcels over time (last 30 days)</summary>
        [HttpGet("parcels-over-time")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetParcelsOverTime()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();
                var thirtyDaysAgo = DateTime.Today.AddDays(-30);

                var grouped = parcels
                    .Where(p => p.Date_sent >= thirtyDaysAgo)
                    .GroupBy(p => p.Date_sent.Date)
                    .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), count = g.Count() })
                    .OrderBy(x => x.date)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parcels over time");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Payment method breakdown</summary>
        [HttpGet("payment-methods")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetPaymentMethods()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var grouped = parcels
                    .GroupBy(p => p.Payment_Method.ToString())
                    .Select(g => new { method = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment methods");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>From/To location breakdown</summary>
        [HttpGet("parcels-by-location")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetParcelsByLocation()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var fromLocations = parcels
                    .GroupBy(p => p.From ?? "Unknown")
                    .Select(g => new { location = g.Key, type = "From", count = g.Count() });

                var toLocations = parcels
                    .GroupBy(p => p.To ?? "Unknown")
                    .Select(g => new { location = g.Key, type = "To", count = g.Count() });

                var combined = fromLocations.Concat(toLocations)
                    .GroupBy(x => new { x.location, x.type })
                    .Select(g => new { location = g.Key.location, type = g.Key.type, count = g.Sum(x => x.count) })
                    .OrderByDescending(x => x.count)
                    .Take(20)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = combined });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parcels by location");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Parcels grouped by location AND status (stacked bar)</summary>
        [HttpGet("parcels-by-location-status")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetParcelsByLocationStatus()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var grouped = parcels
                    .GroupBy(p => new { Location = p.From ?? "Unknown", Status = p.Status.ToString() })
                    .Select(g => new { location = g.Key.Location, status = g.Key.Status, count = g.Count() })
                    .OrderBy(x => x.location)
                    .ThenBy(x => x.status)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parcels by location status");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Batches grouped by status</summary>
        [HttpGet("batches-by-status")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetBatchesByStatus()
        {
            try
            {
                var client = GetClient();
                if (client.NavBatchService == null)
                    return Ok(new Results<object[]> { Code = 0, Contents = Array.Empty<object>() });

                var batches = await client.NavBatchService.ReadMultipleBatchesAsync(null, 0)
                    ?? Array.Empty<P_Batches.ParcelBatches>();

                var grouped = batches
                    .GroupBy(b => b.Status.ToString())
                    .Select(g => new { status = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batches by status");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Who to pay (Sender vs Receiver)</summary>
        [HttpGet("who-to-pay")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetWhoToPay()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var grouped = parcels
                    .GroupBy(p => p.Who_to_Pay.ToString())
                    .Select(g => new { who = g.Key, count = g.Count() })
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting who to pay");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Paid vs Unpaid</summary>
        [HttpGet("paid-vs-unpaid")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetPaidVsUnpaid()
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var paid = parcels.Count(p => p.Paid == true);
                var unpaid = parcels.Length - paid;

                return Ok(new Results<object[]>
                {
                    Code = 0,
                    Contents = new object[]
                    {
                        new { label = "Paid", count = paid },
                        new { label = "Unpaid", count = unpaid }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paid vs unpaid");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Recent parcels</summary>
        [HttpGet("recent-parcels")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetRecentParcels([FromQuery] int count = 20)
        {
            try
            {
                var client = GetClient();
                var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                    ?? Array.Empty<Parcels.Parcel>();

                var recent = parcels
                    .OrderByDescending(p => p.Date_sent)
                    .Take(count)
                    .Select(p => new
                    {
                        documentNo = p.Document_No,
                        sender = p.Sender_Name,
                        receiver = p.Receiver_Name,
                        from = p.From,
                        to = p.To,
                        status = p.Status.ToString(),
                        payment = p.Payment_Method.ToString(),
                        amount = p.Amount_Paid,
                        created = p.Date_sent.ToString("yyyy-MM-dd")
                    })
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = recent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent parcels");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>M-Pesa transaction summary</summary>
        [HttpGet("mpesa-summary")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetMpesaSummary()
        {
            try
            {
                var statuses = await _db.MpesaStkStatuses.ToArrayAsync();
                var total = statuses.Length;
                var successful = statuses.Count(s => s.ResultCode == 0);
                var failed = total - successful;

                return Ok(new Results<object>
                {
                    Code = 0,
                    Contents = new
                    {
                        total,
                        successful,
                        failed,
                        successRate = total > 0 ? Math.Round((double)successful / total * 100, 1) : 0
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting M-Pesa summary");
                return StatusCode(500, Err(ex.Message));
            }
        }

        private IClient GetClient() => (IClient)HttpContext.Items["Client"]!;
        private static Results<object> Err(string msg) => new() { Code = -1, Desc = msg };
    }
}
