using Microsoft.AspNetCore.Mvc;
using ParcelAPI.Clients;
using ParcelAPI.Filters;
using ParcelAPI.Models;
using ParcelAPI.Services;
using NavUsers = User;
using NavLocations = Loc;
using NavBatches = P_Batches;
using NavVehicles = Vehicles;

namespace ParcelAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ServiceFilter(typeof(ClientIdentifierFilter))]
    public class ParcelController : ControllerBase
    {
        private readonly ILogger<ParcelController> _logger;
        private readonly IClientService _clientService;
        private readonly NavSmsService _smsService;

        public ParcelController(
            ILogger<ParcelController> logger,
            IClientService clientService,
            NavSmsService smsService)
        {
            _logger = logger;
            _clientService = clientService;
            _smsService = smsService;
        }

        private string ClientId => HttpContext.Items["ClientId"]?.ToString() ?? string.Empty;
        private IClient Client => (IClient)HttpContext.Items["Client"]!;

        [HttpPost("Parcels")]
        public async Task<ActionResult<Results<Parcels.Parcel[]>>> GetParcels([FromBody] NavParcelRequest? request)
        {
            try
            {
                if (Client.NavParcelService == null)
                    return BadRequest(new Results<Parcels.Parcel[]> { Code = -1, Desc = "NAV Parcel Service not available" });

                var filters = BuildNavFilters(request);
                var parcels = await Client.NavParcelService.ReadMultipleParcelsAsync(filters, request?.PageSize ?? 100);

                return Ok(new Results<Parcels.Parcel[]> { Code = 0, Contents = parcels });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving parcels");
                return StatusCode(500, new Results<Parcels.Parcel[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("Locations")]
        public async Task<ActionResult<Results<NavLocations.Locations[]>>> GetLocations([FromBody] NavLocationRequest? request)
        {
            try
            {
                if (Client.NavLocationService == null)
                    return BadRequest(new Results<NavLocations.Locations[]> { Code = -1, Desc = "NAV Location Service not available" });

                var filters = BuildNavLocationFilters(request);
                var locations = await Client.NavLocationService.ReadMultipleLocationsAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavLocations.Locations[]> { Code = 0, Contents = locations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving locations");
                return StatusCode(500, new Results<NavLocations.Locations[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("Vehicles")]
        public async Task<ActionResult<Results<NavVehicles.VehiclesBasics[]>>> GetVehicles([FromBody] NavVehicleRequest? request)
        {
            try
            {
                if (Client.NavVehicleService == null)
                    return BadRequest(new Results<NavVehicles.VehiclesBasics[]> { Code = -1, Desc = "NAV Vehicle Service not available" });

                var filters = BuildNavVehicleFilters(request);
                var vehicles = await Client.NavVehicleService.ReadMultipleVehiclesAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavVehicles.VehiclesBasics[]> { Code = 0, Contents = vehicles });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving vehicles");
                return StatusCode(500, new Results<NavVehicles.VehiclesBasics[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("Users")]
        public async Task<ActionResult<Results<NavUsers.Parcel_Users[]>>> GetUsers()
        {
            try
            {
                var users = await Client.GetParcelUsersAsync();
                return Ok(new Results<NavUsers.Parcel_Users[]> { Code = 0, Contents = users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users");
                return StatusCode(500, new Results<NavUsers.Parcel_Users[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("Batches")]
        public async Task<ActionResult<Results<NavBatches.ParcelBatches[]>>> GetBatches([FromBody] NavBatchRequest? request)
        {
            try
            {
                if (Client.NavBatchService == null)
                    return BadRequest(new Results<NavBatches.ParcelBatches[]> { Code = -1, Desc = "NAV Batch Service not available" });

                var filters = BuildNavBatchFilters(request);
                var batches = await Client.NavBatchService.ReadMultipleBatchesAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavBatches.ParcelBatches[]> { Code = 0, Contents = batches });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving batches");
                return StatusCode(500, new Results<NavBatches.ParcelBatches[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpGet("nav/parcels/{documentNo}")]
        public async Task<ActionResult<Results<Parcels.Parcel>>> GetNavParcel(string documentNo)
        {
            try
            {
                if (Client.NavParcelService == null)
                    return BadRequest(new Results<Parcels.Parcel> { Code = -1, Desc = "NAV Parcel Service not available" });

                var parcel = await Client.NavParcelService.ReadParcelAsync(documentNo);
                if (parcel == null)
                    return NotFound(new Results<Parcels.Parcel> { Code = -1, Desc = $"Parcel {documentNo} not found" });

                return Ok(new Results<Parcels.Parcel> { Code = 0, Contents = parcel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving parcel {DocumentNo}", documentNo);
                return StatusCode(500, new Results<Parcels.Parcel> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/parcels/create")]
        public async Task<ActionResult<Results<Parcels.Parcel>>> CreateNavParcel([FromBody] Parcels.Parcel parcel)
        {
            try
            {
                if (Client.NavParcelService == null)
                    return BadRequest(new Results<Parcels.Parcel> { Code = -1, Desc = "NAV Parcel Service not available" });

                ApplyParcelSpecifiedFlags(parcel);

                var createdParcel = await Client.NavParcelService.CreateParcelAsync(parcel);
                return Ok(new Results<Parcels.Parcel> { Code = 0, Desc = $"Parcel created: {createdParcel.Document_No}", Contents = createdParcel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating parcel");
                return StatusCode(500, new Results<Parcels.Parcel> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPut("nav/parcels/update")]
        [HttpPost("nav/parcels/update")]
        public async Task<ActionResult<Results<Parcels.Parcel>>> UpdateNavParcel([FromBody] Parcels.Parcel parcel)
        {
            try
            {
                if (Client.NavParcelService == null)
                    return BadRequest(new Results<Parcels.Parcel> { Code = -1, Desc = "NAV Parcel Service not available" });

                ApplyParcelSpecifiedFlags(parcel);

                var updatedParcel = await Client.NavParcelService.UpdateParcelAsync(parcel);
                return Ok(new Results<Parcels.Parcel> { Code = 0, Desc = "Parcel updated", Contents = updatedParcel });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating parcel");
                return StatusCode(500, new Results<Parcels.Parcel> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/parcels/update-post")]
        public Task<ActionResult<Results<Parcels.Parcel>>> UpdateNavParcelPost([FromBody] Parcels.Parcel parcel)
        {
            return UpdateNavParcel(parcel);
        }

        [HttpDelete("nav/parcels/{key}")]
        public async Task<ActionResult<Results<bool>>> DeleteNavParcel(string key)
        {
            try
            {
                if (Client.NavParcelService == null)
                    return BadRequest(new Results<bool> { Code = -1, Desc = "NAV Parcel Service not available" });

                var deleted = await Client.NavParcelService.DeleteParcelAsync(key);
                return Ok(new Results<bool> { Code = deleted ? 0 : -1, Desc = deleted ? "Deleted" : "Failed", Contents = deleted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting parcel");
                return StatusCode(500, new Results<bool> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpDelete("nav/parcels/by-document/{documentNo}")]
        public async Task<ActionResult<Results<bool>>> DeleteNavParcelByDocumentNo(string documentNo)
        {
            try
            {
                if (Client.NavParcelService == null)
                    return BadRequest(new Results<bool> { Code = -1, Desc = "NAV Parcel Service not available" });

                var parcel = await Client.NavParcelService.ReadParcelAsync(documentNo);
                if (parcel == null)
                    return NotFound(new Results<bool> { Code = -1, Desc = $"Parcel {documentNo} not found" });

                var deleted = await Client.NavParcelService.DeleteParcelAsync(parcel.Key);
                return Ok(new Results<bool> { Code = deleted ? 0 : -1, Desc = deleted ? "Deleted" : "Failed", Contents = deleted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting parcel {DocumentNo}", documentNo);
                return StatusCode(500, new Results<bool> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/users")]
        public async Task<ActionResult<Results<NavUsers.Parcel_Users[]>>> GetNavUsers([FromBody] NavUserRequest? request)
        {
            try
            {
                if (Client.NavUserService == null)
                    return BadRequest(new Results<NavUsers.Parcel_Users[]> { Code = -1, Desc = "NAV User Service not available" });

                var filters = BuildNavUserFilters(request);
                var users = await Client.NavUserService.ReadMultipleUsersAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavUsers.Parcel_Users[]> { Code = 0, Contents = users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users from NAV");
                return StatusCode(500, new Results<NavUsers.Parcel_Users[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpGet("nav/users/{agentCode}")]
        public async Task<ActionResult<Results<NavUsers.Parcel_Users>>> GetNavUser(string agentCode)
        {
            try
            {
                if (Client.NavUserService == null)
                    return BadRequest(new Results<NavUsers.Parcel_Users> { Code = -1, Desc = "NAV User Service not available" });

                var user = await Client.NavUserService.ReadUserAsync(agentCode);
                if (user == null)
                    return NotFound(new Results<NavUsers.Parcel_Users> { Code = -1, Desc = $"User {agentCode} not found" });

                return Ok(new Results<NavUsers.Parcel_Users> { Code = 0, Contents = user });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user {AgentCode}", agentCode);
                return StatusCode(500, new Results<NavUsers.Parcel_Users> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/users/change-password")]
        public async Task<ActionResult<Results<NavUsers.Parcel_Users>>> ChangeUserPassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request?.AgentCode) || string.IsNullOrEmpty(request?.Password))
                    return BadRequest(new Results<NavUsers.Parcel_Users> { Code = -1, Desc = "AgentCode and Password are required" });

                if (Client.NavUserService == null)
                    return BadRequest(new Results<NavUsers.Parcel_Users> { Code = -1, Desc = "NAV User Service not available" });

                var updatedUser = await Client.NavUserService.ChangePasswordAsync(request.AgentCode, request.Password);
                if (updatedUser == null)
                    return NotFound(new Results<NavUsers.Parcel_Users> { Code = -1, Desc = $"User {request.AgentCode} not found" });

                updatedUser.Password = string.Empty;

                return Ok(new Results<NavUsers.Parcel_Users> { Code = 0, Desc = "Password changed", Contents = updatedUser });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for {AgentCode}", request?.AgentCode);
                return StatusCode(500, new Results<NavUsers.Parcel_Users> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/users/create")]
        public async Task<ActionResult<Results<NavUsers.Parcel_Users>>> CreateNavUser([FromBody] CreateNavUserRequest request)
        {
            try
            {
                if (Client.NavUserService == null)
                    return BadRequest(new Results<NavUsers.Parcel_Users> { Code = -1, Desc = "NAV User Service not available" });

                if (string.IsNullOrWhiteSpace(request.AgentCode) ||
                    string.IsNullOrWhiteSpace(request.Name) ||
                    string.IsNullOrWhiteSpace(request.MobileNo) ||
                    string.IsNullOrWhiteSpace(request.Password) ||
                    string.IsNullOrWhiteSpace(request.Location) ||
                    string.IsNullOrWhiteSpace(request.CreatedByAgentCode))
                {
                    return BadRequest(new Results<NavUsers.Parcel_Users>
                    {
                        Code = -1,
                        Desc = "AgentCode, Name, MobileNo, Password, Location and CreatedByAgentCode are required"
                    });
                }

                if (!TryParseAccountType(request.AccountType, out var accountType))
                {
                    return BadRequest(new Results<NavUsers.Parcel_Users>
                    {
                        Code = -1,
                        Desc = "Invalid account type"
                    });
                }

                var creator = await Client.NavUserService.ReadUserAsync(request.CreatedByAgentCode.Trim());
                if (creator == null)
                {
                    return NotFound(new Results<NavUsers.Parcel_Users>
                    {
                        Code = -1,
                        Desc = $"Creator user {request.CreatedByAgentCode} not found"
                    });
                }

                if (creator.Account_type != NavUsers.Account_type.Admin)
                {
                    return StatusCode(403, new Results<NavUsers.Parcel_Users>
                    {
                        Code = -1,
                        Desc = "Only Admin users can create new users"
                    });
                }

                var created = await Client.NavUserService.CreateUserAsync(
                    agentCode: request.AgentCode,
                    name: request.Name,
                    mobileNo: request.MobileNo,
                    password: request.Password,
                    location: request.Location,
                    accountType: accountType,
                    enteredBy: request.CreatedByAgentCode);

                created.Password = string.Empty;

                return Ok(new Results<NavUsers.Parcel_Users>
                {
                    Code = 0,
                    Desc = "User created",
                    Contents = created
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user {AgentCode}", request?.AgentCode);
                return StatusCode(500, new Results<NavUsers.Parcel_Users> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/locations")]
        public async Task<ActionResult<Results<NavLocations.Locations[]>>> GetNavLocations([FromBody] NavLocationRequest? request)
        {
            try
            {
                if (Client.NavLocationService == null)
                    return BadRequest(new Results<NavLocations.Locations[]> { Code = -1, Desc = "NAV Location Service not available" });

                var filters = BuildNavLocationFilters(request);
                var locations = await Client.NavLocationService.ReadMultipleLocationsAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavLocations.Locations[]> { Code = 0, Contents = locations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving locations from NAV");
                return StatusCode(500, new Results<NavLocations.Locations[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpGet("nav/locations/{code}")]
        public async Task<ActionResult<Results<NavLocations.Locations>>> GetNavLocation(string code)
        {
            try
            {
                if (Client.NavLocationService == null)
                    return BadRequest(new Results<NavLocations.Locations> { Code = -1, Desc = "NAV Location Service not available" });

                var location = await Client.NavLocationService.ReadLocationAsync(code);
                if (location == null)
                    return NotFound(new Results<NavLocations.Locations> { Code = -1, Desc = $"Location {code} not found" });

                return Ok(new Results<NavLocations.Locations> { Code = 0, Contents = location });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving location {Code}", code);
                return StatusCode(500, new Results<NavLocations.Locations> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/vehicles")]
        public async Task<ActionResult<Results<NavVehicles.VehiclesBasics[]>>> GetNavVehicles([FromBody] NavVehicleRequest? request)
        {
            try
            {
                if (Client.NavVehicleService == null)
                    return BadRequest(new Results<NavVehicles.VehiclesBasics[]> { Code = -1, Desc = "NAV Vehicle Service not available" });

                var filters = BuildNavVehicleFilters(request);
                var vehicles = await Client.NavVehicleService.ReadMultipleVehiclesAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavVehicles.VehiclesBasics[]> { Code = 0, Contents = vehicles });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving vehicles from NAV");
                return StatusCode(500, new Results<NavVehicles.VehiclesBasics[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/batches")]
        public async Task<ActionResult<Results<NavBatches.ParcelBatches[]>>> GetNavBatches([FromBody] NavBatchRequest? request)
        {
            try
            {
                if (Client.NavBatchService == null)
                    return BadRequest(new Results<NavBatches.ParcelBatches[]> { Code = -1, Desc = "NAV Batch Service not available" });

                var filters = BuildNavBatchFilters(request);
                var batches = await Client.NavBatchService.ReadMultipleBatchesAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<NavBatches.ParcelBatches[]> { Code = 0, Contents = batches });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving batches from NAV");
                return StatusCode(500, new Results<NavBatches.ParcelBatches[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpGet("nav/batches/{batchNo}")]
        public async Task<ActionResult<Results<NavBatches.ParcelBatches>>> GetNavBatch(string batchNo)
        {
            try
            {
                if (Client.NavBatchService == null)
                    return BadRequest(new Results<NavBatches.ParcelBatches> { Code = -1, Desc = "NAV Batch Service not available" });

                var batch = await Client.NavBatchService.ReadBatchAsync(batchNo);
                if (batch == null)
                    return NotFound(new Results<NavBatches.ParcelBatches> { Code = -1, Desc = $"Batch {batchNo} not found" });

                return Ok(new Results<NavBatches.ParcelBatches> { Code = 0, Contents = batch });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving batch {BatchNo}", batchNo);
                return StatusCode(500, new Results<NavBatches.ParcelBatches> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/batches/create")]
        public async Task<ActionResult<Results<NavBatches.ParcelBatches>>> CreateNavBatch([FromBody] NavBatches.ParcelBatches batch)
        {
            try
            {
                if (Client.NavBatchService == null)
                    return BadRequest(new Results<NavBatches.ParcelBatches> { Code = -1, Desc = "NAV Batch Service not available" });

                ApplyBatchSpecifiedFlags(batch);

                var createdBatch = await Client.NavBatchService.CreateBatchAsync(batch);
                return Ok(new Results<NavBatches.ParcelBatches>
                {
                    Code = 0,
                    Desc = $"Batch created: {createdBatch.Batch_No}",
                    Contents = createdBatch
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating batch");
                return StatusCode(500, new Results<NavBatches.ParcelBatches> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPut("nav/batches/update")]
        [HttpPost("nav/batches/update")]
        public async Task<ActionResult<Results<NavBatches.ParcelBatches>>> UpdateNavBatch([FromBody] NavBatches.ParcelBatches batch)
        {
            try
            {
                if (Client.NavBatchService == null)
                    return BadRequest(new Results<NavBatches.ParcelBatches> { Code = -1, Desc = "NAV Batch Service not available" });

                ApplyBatchSpecifiedFlags(batch);

                var updatedBatch = await Client.NavBatchService.UpdateBatchAsync(batch);
                return Ok(new Results<NavBatches.ParcelBatches> { Code = 0, Desc = "Batch updated", Contents = updatedBatch });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating batch");
                return StatusCode(500, new Results<NavBatches.ParcelBatches> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/batches/update-post")]
        public Task<ActionResult<Results<NavBatches.ParcelBatches>>> UpdateNavBatchPost([FromBody] NavBatches.ParcelBatches batch)
        {
            return UpdateNavBatch(batch);
        }

        [HttpDelete("nav/batches/{key}")]
        public async Task<ActionResult<Results<bool>>> DeleteNavBatch(string key)
        {
            try
            {
                if (Client.NavBatchService == null)
                    return BadRequest(new Results<bool> { Code = -1, Desc = "NAV Batch Service not available" });

                var deleted = await Client.NavBatchService.DeleteBatchAsync(key);
                return Ok(new Results<bool> { Code = deleted ? 0 : -1, Desc = deleted ? "Deleted" : "Failed", Contents = deleted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting batch");
                return StatusCode(500, new Results<bool> { Code = -1, Desc = ex.Message });
            }
        }

        // ==================== Parcel Logs Endpoints ====================

        [HttpPost("nav/logs")]
        public async Task<ActionResult<Results<ParcelLogs.Parcel_Logs[]>>> GetParcelLogs([FromBody] NavParcelLogsRequest? request)
        {
            try
            {
                if (Client.NavParcelLogsService == null)
                    return BadRequest(new Results<ParcelLogs.Parcel_Logs[]> { Code = -1, Desc = "NAV Parcel Logs Service not available" });

                var filters = BuildNavParcelLogsFilters(request);
                var logs = await Client.NavParcelLogsService.ReadMultipleLogsAsync(filters, request?.PageSize ?? 100);
                return Ok(new Results<ParcelLogs.Parcel_Logs[]> { Code = 0, Contents = logs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving parcel logs from NAV");
                return StatusCode(500, new Results<ParcelLogs.Parcel_Logs[]> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("nav/logs/create")]
        public async Task<ActionResult<Results<ParcelLogs.Parcel_Logs>>> CreateParcelLog([FromBody] ParcelLogs.Parcel_Logs log)
        {
            try
            {
                if (Client.NavParcelLogsService == null)
                    return BadRequest(new Results<ParcelLogs.Parcel_Logs> { Code = -1, Desc = "NAV Parcel Logs Service not available" });

                var createdLog = await Client.NavParcelLogsService.CreateLogAsync(log);
                return Ok(new Results<ParcelLogs.Parcel_Logs> { Code = 0, Desc = "Log created", Contents = createdLog });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating parcel log in NAV");
                return StatusCode(500, new Results<ParcelLogs.Parcel_Logs> { Code = -1, Desc = ex.Message });
            }
        }

        private Parcels.Parcel_Filter[]? BuildNavFilters(NavParcelRequest? request)
        {
            if (request == null) return null;
            var filters = new List<Parcels.Parcel_Filter>();
            if (!string.IsNullOrEmpty(request.DocumentNo))
                filters.Add(new Parcels.Parcel_Filter { Field = Parcels.Parcel_Fields.Document_No, Criteria = request.DocumentNo });
            if (!string.IsNullOrEmpty(request.SenderName))
                filters.Add(new Parcels.Parcel_Filter { Field = Parcels.Parcel_Fields.Sender_Name, Criteria = $"*{request.SenderName}*" });
            if (!string.IsNullOrEmpty(request.ReceiverName))
                filters.Add(new Parcels.Parcel_Filter { Field = Parcels.Parcel_Fields.Receiver_Name, Criteria = $"*{request.ReceiverName}*" });
            if (request.Status.HasValue)
                filters.Add(new Parcels.Parcel_Filter { Field = Parcels.Parcel_Fields.Status, Criteria = request.Status.Value.ToString() });
            if (request.DateFrom.HasValue)
                filters.Add(new Parcels.Parcel_Filter { Field = Parcels.Parcel_Fields.Date_sent, Criteria = $">={request.DateFrom.Value:yyyy-MM-dd}" });
            if (request.DateTo.HasValue)
                filters.Add(new Parcels.Parcel_Filter { Field = Parcels.Parcel_Fields.Date_sent, Criteria = $"<={request.DateTo.Value:yyyy-MM-dd}" });
            return filters.Count > 0 ? filters.ToArray() : null;
        }

        private static bool HasDateValue(DateTime value)
        {
            return value > DateTime.MinValue;
        }

        private static void ApplyParcelSpecifiedFlags(Parcels.Parcel parcel)
        {
            parcel.Date_sentSpecified = HasDateValue(parcel.Date_sent);
            parcel.StatusSpecified = true;
            parcel.Who_to_PaySpecified = true;
            parcel.Amount_PaidSpecified = true;
            parcel.PaidSpecified = true;
            parcel.Date_CollectedSpecified = HasDateValue(parcel.Date_Collected);
            parcel.Date_DeliveredSpecified = HasDateValue(parcel.Date_Delivered);
            parcel.Time_CreatedSpecified = HasDateValue(parcel.Time_Created);
            parcel.Time_SentSpecified = HasDateValue(parcel.Time_Sent);
            parcel.Time_CollectedSpecified = HasDateValue(parcel.Time_Collected);
            parcel.Time_DeliveredSpecified = HasDateValue(parcel.Time_Delivered);
            parcel.Payment_MethodSpecified = true;
        }

        private static void ApplyBatchSpecifiedFlags(NavBatches.ParcelBatches batch)
        {
            batch.DateSpecified = HasDateValue(batch.Date);
            batch.StatusSpecified = true;
            batch.Parcel_CountSpecified = true;
            batch.Total_AmountSpecified = true;
            batch.Is_SyncedSpecified = true;
            batch.Created_AtSpecified = HasDateValue(batch.Created_At);
            batch.Updated_AtSpecified = HasDateValue(batch.Updated_At);
            batch.Dispatch_Date_TimeSpecified = HasDateValue(batch.Dispatch_Date_Time);
            batch.Received_Date_TimeSpecified = HasDateValue(batch.Received_Date_Time);
        }

        private NavUsers.Parcel_Users_Filter[]? BuildNavUserFilters(NavUserRequest? request)
        {
            if (request == null) return null;
            var filters = new List<NavUsers.Parcel_Users_Filter>();
            if (!string.IsNullOrEmpty(request.AgentCode))
                filters.Add(new NavUsers.Parcel_Users_Filter { Field = NavUsers.Parcel_Users_Fields.Agent_Code, Criteria = request.AgentCode });
            if (!string.IsNullOrEmpty(request.Name))
                filters.Add(new NavUsers.Parcel_Users_Filter { Field = NavUsers.Parcel_Users_Fields.Name, Criteria = $"*{request.Name}*" });
            if (!string.IsNullOrEmpty(request.MobileNo))
                filters.Add(new NavUsers.Parcel_Users_Filter { Field = NavUsers.Parcel_Users_Fields.Mobile_No, Criteria = request.MobileNo });
            if (request.AccountType.HasValue)
                filters.Add(new NavUsers.Parcel_Users_Filter { Field = NavUsers.Parcel_Users_Fields.Account_type, Criteria = request.AccountType.Value.ToString() });
            return filters.Count > 0 ? filters.ToArray() : null;
        }

        private NavLocations.Locations_Filter[]? BuildNavLocationFilters(NavLocationRequest? request)
        {
            if (request == null) return null;
            var filters = new List<NavLocations.Locations_Filter>();
            if (!string.IsNullOrEmpty(request.Code))
                filters.Add(new NavLocations.Locations_Filter { Field = NavLocations.Locations_Fields.Code, Criteria = request.Code });
            if (!string.IsNullOrEmpty(request.Name))
                filters.Add(new NavLocations.Locations_Filter { Field = NavLocations.Locations_Fields.Name, Criteria = $"*{request.Name}*" });
            return filters.Count > 0 ? filters.ToArray() : null;
        }

        private static bool TryParseAccountType(string? value, out NavUsers.Account_type accountType)
        {
            accountType = NavUsers.Account_type.User;
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var clean = value.Trim();
            if (Enum.TryParse<NavUsers.Account_type>(clean, true, out var parsed))
            {
                accountType = parsed;
                return true;
            }

            if (int.TryParse(clean, out var idx) &&
                Enum.IsDefined(typeof(NavUsers.Account_type), idx))
            {
                accountType = (NavUsers.Account_type)idx;
                return true;
            }

            return false;
        }

        private NavBatches.ParcelBatches_Filter[]? BuildNavBatchFilters(NavBatchRequest? request)
        {
            if (request == null) return null;
            var filters = new List<NavBatches.ParcelBatches_Filter>();

            if (!string.IsNullOrEmpty(request.BatchNo))
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.Batch_No, Criteria = request.BatchNo });
            if (!string.IsNullOrEmpty(request.User))
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.User, Criteria = request.User });
            if (!string.IsNullOrEmpty(request.UserAgentCode))
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.User_Agent_Code, Criteria = request.UserAgentCode });
            if (!string.IsNullOrEmpty(request.FromLocation))
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.From_Location, Criteria = request.FromLocation });
            if (!string.IsNullOrEmpty(request.ToLocation))
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.To_Location, Criteria = request.ToLocation });
            if (request.Status.HasValue)
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.Status, Criteria = request.Status.Value.ToString() });
            if (request.IsSynced.HasValue)
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.Is_Synced, Criteria = request.IsSynced.Value ? "Yes" : "No" });
            if (request.DateFrom.HasValue)
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.Date, Criteria = $">={request.DateFrom.Value:yyyy-MM-dd}" });
            if (request.DateTo.HasValue)
                filters.Add(new NavBatches.ParcelBatches_Filter { Field = NavBatches.ParcelBatches_Fields.Date, Criteria = $"<={request.DateTo.Value:yyyy-MM-dd}" });

            return filters.Count > 0 ? filters.ToArray() : null;
        }

        private NavVehicles.VehiclesBasics_Filter[]? BuildNavVehicleFilters(NavVehicleRequest? request)
        {
            if (request == null) return null;
            var filters = new List<NavVehicles.VehiclesBasics_Filter>();

            if (!string.IsNullOrEmpty(request.VehicleNumber))
                filters.Add(new NavVehicles.VehiclesBasics_Filter { Field = NavVehicles.VehiclesBasics_Fields.Vehicle_Number, Criteria = request.VehicleNumber });
            if (!string.IsNullOrEmpty(request.Code))
                filters.Add(new NavVehicles.VehiclesBasics_Filter { Field = NavVehicles.VehiclesBasics_Fields.Code, Criteria = request.Code });
            if (!string.IsNullOrEmpty(request.Category))
                filters.Add(new NavVehicles.VehiclesBasics_Filter { Field = NavVehicles.VehiclesBasics_Fields.Category, Criteria = request.Category });
            if (!string.IsNullOrEmpty(request.FleetNo))
                filters.Add(new NavVehicles.VehiclesBasics_Filter { Field = NavVehicles.VehiclesBasics_Fields.Fleet_No, Criteria = request.FleetNo });
            if (request.Status.HasValue)
                filters.Add(new NavVehicles.VehiclesBasics_Filter { Field = NavVehicles.VehiclesBasics_Fields.Status, Criteria = request.Status.Value.ToString() });
            if (request.VehicleType.HasValue)
                filters.Add(new NavVehicles.VehiclesBasics_Filter { Field = NavVehicles.VehiclesBasics_Fields.Vehicle_Type, Criteria = request.VehicleType.Value.ToString() });

            return filters.Count > 0 ? filters.ToArray() : null;
        }

        private ParcelLogs.Parcel_Logs_Filter[]? BuildNavParcelLogsFilters(NavParcelLogsRequest? request)
        {
            if (request == null) return null;
            var filters = new List<ParcelLogs.Parcel_Logs_Filter>();
            if (!string.IsNullOrEmpty(request.DocumentNo))
                filters.Add(new ParcelLogs.Parcel_Logs_Filter { Field = ParcelLogs.Parcel_Logs_Fields.Document_No, Criteria = request.DocumentNo });
            if (!string.IsNullOrEmpty(request.User))
                filters.Add(new ParcelLogs.Parcel_Logs_Filter { Field = ParcelLogs.Parcel_Logs_Fields.User, Criteria = request.User });
            if (!string.IsNullOrEmpty(request.Action))
                filters.Add(new ParcelLogs.Parcel_Logs_Filter { Field = ParcelLogs.Parcel_Logs_Fields.Action, Criteria = request.Action });
            return filters.Count > 0 ? filters.ToArray() : null;
        }

    public class NavParcelRequest
    {
        public string? DocumentNo { get; set; }
        public string? SenderName { get; set; }
        public string? ReceiverName { get; set; }
        public Parcels.Status? Status { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int PageSize { get; set; } = 100;
    }

    public class NavUserRequest
    {
        public string? AgentCode { get; set; }
        public string? Name { get; set; }
        public string? MobileNo { get; set; }
        public User.Account_type? AccountType { get; set; }
        public int PageSize { get; set; } = 100;
    }

    public class NavLocationRequest
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public int PageSize { get; set; } = 100;
    }

    public class NavVehicleRequest
    {
        public string? VehicleNumber { get; set; }
        public string? Code { get; set; }
        public string? Category { get; set; }
        public string? FleetNo { get; set; }
        public NavVehicles.Status? Status { get; set; }
        public NavVehicles.Vehicle_Type? VehicleType { get; set; }
        public int PageSize { get; set; } = 100;
    }

    public class NavBatchRequest
    {
        public string? BatchNo { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? User { get; set; }
        public NavBatches.Status? Status { get; set; }
        public string? UserAgentCode { get; set; }
        public string? FromLocation { get; set; }
        public string? ToLocation { get; set; }
        public bool? IsSynced { get; set; }
        public int PageSize { get; set; } = 100;
    }

    public class ChangePasswordRequest
    {
        public string AgentCode { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class CreateNavUserRequest
    {
        public string AgentCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MobileNo { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string AccountType { get; set; } = "User";
        public string CreatedByAgentCode { get; set; } = string.Empty;
    }

        [HttpPost("sms/send")]
        public async Task<ActionResult<Results<object>>> SendSms([FromBody] SmsSendRequest request)
        {
            try
            {
                var result = await _smsService.SendBulkAsync(
                    new List<ParcelAPI.Models.SmsRequest> { new() { Phone = request.Phone, Message = request.Message } },
                    ClientId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS");
                return StatusCode(500, new Results<object> { Code = -1, Desc = ex.Message });
            }
        }

        [HttpPost("sms/send-bulk")]
        public async Task<ActionResult<Results<object>>> SendBulkSms([FromBody] SmsSendBulkRequest request)
        {
            try
            {
                var messages = request.Messages.Select(m => new ParcelAPI.Models.SmsRequest
                {
                    Phone = m.Phone,
                    Message = m.Message
                }).ToList();
                var result = await _smsService.SendBulkAsync(messages, ClientId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending bulk SMS");
                return StatusCode(500, new Results<object> { Code = -1, Desc = ex.Message });
            }
        }

    public class NavParcelLogsRequest
    {
        public string? DocumentNo { get; set; }
        public string? User { get; set; }
        public string? Action { get; set; }
        public int PageSize { get; set; } = 100;
    }

    public class SmsSendRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class SmsSendBulkRequest
    {
        public List<SmsSendRequest> Messages { get; set; } = new();
    }
}
}
