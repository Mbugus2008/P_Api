using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ParcelAPI.Models;

namespace ParcelAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AppUpdateController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AppUpdateController> _logger;
        private readonly IWebHostEnvironment _environment;

        public AppUpdateController(
            IConfiguration configuration,
            ILogger<AppUpdateController> logger,
            IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
        }

        /// <summary>
        /// Get the latest Android app version info.
        /// Does NOT require X-Client-Identifier header.
        /// </summary>
        [HttpGet("android")]
        [ProducesResponseType(typeof(Results<AppVersionInfo>), StatusCodes.Status200OK)]
        public IActionResult GetAndroidVersion()
        {
            try
            {
                var versionSection = _configuration.GetSection("AppVersion");
                var request = HttpContext.Request;
                var baseUrl = $"{request.Scheme}://{request.Host}";

                var versionInfo = new AppVersionInfo
                {
                    Version = versionSection["Version"] ?? "1.0.0",
                    VersionCode = int.TryParse(versionSection["VersionCode"], out var code) ? code : 1,
                    BuildDate = versionSection["BuildDate"] ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    DownloadUrl = versionSection["DownloadUrl"] ?? $"{baseUrl}/ParcelApp/ParcelApp.apk",
                    ReleaseNotes = versionSection["ReleaseNotes"],
                    ForceUpdate = bool.TryParse(versionSection["ForceUpdate"], out var force) && force
                };

                return Ok(new Results<AppVersionInfo>
                {
                    Code = 0,
                    Desc = "Success",
                    Contents = versionInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving app version info");
                return StatusCode(500, new Results<object>
                {
                    Code = -1,
                    Desc = "Failed to retrieve version info"
                });
            }
        }
    }
}
