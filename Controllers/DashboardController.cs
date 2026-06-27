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

        private async Task<Parcels.Parcel[]> GetParcelsAsync(DateTime? from = null, DateTime? to = null)
        {
            var client = GetClient();
            var parcels = await client.NavParcelService.ReadMultipleParcelsAsync(null, 0)
                ?? Array.Empty<Parcels.Parcel>();

            if (from.HasValue)
                parcels = parcels.Where(p => p.Date_sent >= from.Value).ToArray();
            if (to.HasValue)
                parcels = parcels.Where(p => p.Date_sent <= to.Value).ToArray();

            return parcels;
        }

        private async Task<P_Batches.ParcelBatches[]> GetBatchesAsync()
        {
            var client = GetClient();
            if (client.NavBatchService == null)
                return Array.Empty<P_Batches.ParcelBatches>();
            return await client.NavBatchService.ReadMultipleBatchesAsync(null, 0)
                ?? Array.Empty<P_Batches.ParcelBatches>();
        }

        /// <summary>Summary stats</summary>
        [HttpGet("summary")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetSummary([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);
                var allParcels = await GetParcelsAsync(); // all-time, no date filter
                var today = DateTime.Today;
                var totalRevenue = parcels.Sum(p => p.Parcel_Value);
                var paidAmount = parcels.Where(p => p.Paid == true).Sum(p => p.Amount_Paid);
                var allTotalRevenue = allParcels.Sum(p => p.Parcel_Value);
                var allPaid = allParcels.Where(p => p.Paid == true).Sum(p => p.Amount_Paid);

                return Ok(new Results<object>
                {
                    Code = 0,
                    Contents = new
                    {
                        totalParcels = parcels.Length,
                        totalRevenue = totalRevenue,
                        paidAmount = paidAmount,
                        unpaidAmount = totalRevenue - paidAmount,
                        totalOutstanding = allTotalRevenue - allPaid,
                        previousDaysPaidToday = parcels
                            .Where(p => p.Paid == true
                                && p.Date_sent.Date < today
                                && p.Payment_Date > DateTime.MinValue
                                && p.Payment_Date.Date == today)
                            .Sum(p => p.Amount_Paid),
                        pending = parcels.Count(p => p.Status == Parcels.Status.Open),
                        inTransit = parcels.Count(p => p.Status == Parcels.Status.In_Transist),
                        received = parcels.Count(p => p.Status == Parcels.Status.Waiting_Collection),
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
        public async Task<ActionResult<Results<object[]>>> GetParcelsByStatus([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

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
        public async Task<ActionResult<Results<object[]>>> GetParcelsByLocation([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

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
        public async Task<ActionResult<Results<object[]>>> GetParcelsByLocationStatus([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

                var grouped = parcels
                    .GroupBy(p => new { Location = p.To ?? "Unknown", Status = p.Status.ToString() })
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

        /// <summary>Revenue grouped by From location</summary>
        [HttpGet("revenue-by-location")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetRevenueByLocation([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

                var grouped = parcels
                    .GroupBy(p => p.From ?? "Unknown")
                    .Select(g => new { location = g.Key, revenue = g.Sum(p => p.Amount_Paid), count = g.Count() })
                    .OrderByDescending(x => x.revenue)
                    .Take(15)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting revenue by location");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Revenue grouped by vehicle</summary>
        [HttpGet("revenue-by-vehicle")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetRevenueByVehicle([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

                var grouped = parcels
                    .Where(p => !string.IsNullOrEmpty(p.Vehicle))
                    .GroupBy(p => p.Vehicle!.Trim())
                    .Select(g => new { vehicle = g.Key, revenue = g.Sum(p => p.Amount_Paid), count = g.Count() })
                    .OrderByDescending(x => x.revenue)
                    .Take(15)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting revenue by vehicle");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Parcel count grouped by vehicle</summary>
        [HttpGet("parcels-by-vehicle")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetParcelsByVehicle([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

                var grouped = parcels
                    .Where(p => !string.IsNullOrEmpty(p.Vehicle))
                    .GroupBy(p => p.Vehicle!.Trim())
                    .Select(g => new { vehicle = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .Take(15)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting parcels by vehicle");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Daily revenue and parcel count</summary>
        [HttpGet("daily-revenue")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object[]>>> GetDailyRevenue([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);

                var grouped = parcels
                    .GroupBy(p => p.Date_sent.Date)
                    .Select(g => new
                    {
                        date = g.Key.ToString("yyyy-MM-dd"),
                        revenue = g.Sum(p => p.Amount_Paid),
                        parcels = g.Count()
                    })
                    .OrderBy(x => x.date)
                    .ToArray();

                return Ok(new Results<object[]> { Code = 0, Contents = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting daily revenue");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Comparative summary: today vs yesterday with % change</summary>
        [HttpGet("comparative-summary")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetComparativeSummary([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var today = to ?? DateTime.Today;
                var todayStart = from ?? today;
                var yesterdayStart = todayStart.AddDays(-1);
                var yesterdayEnd = today.AddDays(-1);

                var todayParcels = await GetParcelsAsync(todayStart, today);
                var yesterdayParcels = await GetParcelsAsync(yesterdayStart, yesterdayEnd);
                var batches = await GetBatchesAsync();

                return Ok(new Results<object>
                {
                    Code = 0,
                    Contents = new
                    {
                        today = new
                        {
                            parcels = todayParcels.Length,
                            revenue = todayParcels.Sum(p => p.Amount_Paid),
                            inTransit = batches.Count(b => b.Status == P_Batches.Status.Intransit),
                            awaiting = todayParcels.Count(p => p.Status == Parcels.Status.Waiting_Collection),
                            collected = todayParcels.Count(p => p.Status == Parcels.Status.Collected)
                        },
                        yesterday = new
                        {
                            parcels = yesterdayParcels.Length,
                            revenue = yesterdayParcels.Sum(p => p.Amount_Paid),
                            collected = yesterdayParcels.Count(p => p.Status == Parcels.Status.Collected)
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting comparative summary");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Stuck parcels: in transit for 2+ days</summary>
        [HttpGet("stuck-parcels")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetStuckParcels()
        {
            try
            {
                var parcels = await GetParcelsAsync();
                var cutoff = DateTime.Today.AddDays(-2);
                var stuck = parcels
                    .Where(p => p.Status == Parcels.Status.In_Transist && p.Date_sent <= cutoff)
                    .Select(p => new
                    {
                        p.Document_No,
                        p.From,
                        p.To,
                        p.Sender_Name,
                        p.Receiver_Name,
                        daysInTransit = (DateTime.Today - p.Date_sent.Date).Days
                    })
                    .OrderByDescending(p => p.daysInTransit)
                    .ToArray();

                return Ok(new Results<object> { Code = 0, Contents = new { count = stuck.Length, parcels = stuck } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stuck parcels");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Unpaid parcels summary</summary>
        [HttpGet("unpaid-summary")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetUnpaidSummary()
        {
            try
            {
                var parcels = await GetParcelsAsync();
                var unpaid = parcels.Where(p => p.Paid == false).ToArray();

                return Ok(new Results<object>
                {
                    Code = 0,
                    Contents = new
                    {
                        count = unpaid.Length,
                        totalAmount = unpaid.Sum(p => p.Amount_Paid)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unpaid summary");
                return StatusCode(500, Err(ex.Message));
            }
        }

        /// <summary>Analytics: avg delivery & collection times</summary>
        [HttpGet("analytics")]
        [ServiceFilter(typeof(ClientIdentifierFilter))]
        public async Task<ActionResult<Results<object>>> GetAnalytics([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var parcels = await GetParcelsAsync(from, to);
                var delivered = parcels.Where(p => p.Date_Delivered.Year > 1).ToArray();
                var collected = parcels.Where(p => p.Date_Collected.Year > 1).ToArray();

                var avgDeliveryHours = delivered.Any()
                    ? delivered.Average(p => (p.Date_Delivered - p.Date_sent).TotalHours)
                    : 0;
                var avgCollectionHours = collected.Any()
                    ? collected.Average(p => (p.Date_Collected - p.Date_Delivered).TotalHours)
                    : 0;

                return Ok(new Results<object>
                {
                    Code = 0,
                    Contents = new
                    {
                        avgDeliveryHours = Math.Round(avgDeliveryHours, 1),
                        avgCollectionHours = Math.Round(avgCollectionHours, 1),
                        totalDelivered = delivered.Length,
                        totalCollected = collected.Length
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analytics");
                return StatusCode(500, Err(ex.Message));
            }
        }

        private IClient GetClient() => (IClient)HttpContext.Items["Client"]!;
        private static Results<object> Err(string msg) => new() { Code = -1, Desc = msg };
    }
}
