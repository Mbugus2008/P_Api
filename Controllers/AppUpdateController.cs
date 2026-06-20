using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ParcelAPI.Models;
using System.Text.Json;

namespace ParcelAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AppUpdateController : ControllerBase
    {
        private readonly ILogger<AppUpdateController> _logger;
        private readonly IWebHostEnvironment _environment;

        public AppUpdateController(
            ILogger<AppUpdateController> logger,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        /// <summary>
        /// Get the latest Android app version info from app_version.json in the ParcelApp folder.
        /// Does NOT require X-Client-Identifier header.
        /// </summary>
        [HttpGet("android")]
        [ProducesResponseType(typeof(Results<AppVersionInfo>), StatusCodes.Status200OK)]
        public IActionResult GetAndroidVersion()
        {
            try
            {
                var request = HttpContext.Request;
                var baseUrl = $"{request.Scheme}://{request.Host}";
                var versionFilePath = Path.Combine(_environment.ContentRootPath, "ParcelApp", "app_version.json");

                if (!System.IO.File.Exists(versionFilePath))
                {
                    _logger.LogWarning("app_version.json not found at {Path}", versionFilePath);
                    return Ok(new Results<AppVersionInfo>
                    {
                        Code = 0,
                        Desc = "No version file",
                        Contents = new AppVersionInfo
                        {
                            Version = "0.0.0",
                            VersionCode = 0,
                            DownloadUrl = $"{baseUrl}/ParcelApp/ParcelApp.apk"
                        }
                    });
                }

                var json = System.IO.File.ReadAllText(versionFilePath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var versionInfo = new AppVersionInfo
                {
                    Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0",
                    VersionCode = root.TryGetProperty("versionCode", out var vc) ? vc.GetInt32() : 1,
                    BuildDate = root.TryGetProperty("buildDate", out var bd) ? bd.GetString() ?? "" : "",
                    DownloadUrl = root.TryGetProperty("downloadUrl", out var du) ? du.GetString() ?? $"{baseUrl}/ParcelApp/ParcelApp.apk" : $"{baseUrl}/ParcelApp/ParcelApp.apk",
                    ReleaseNotes = root.TryGetProperty("releaseNotes", out var rn) ? rn.GetString() : null,
                    ForceUpdate = root.TryGetProperty("forceUpdate", out var fu) && fu.GetBoolean()
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
                _logger.LogError(ex, "Error reading app version info");
                return StatusCode(500, new Results<object>
                {
                    Code = -1,
                    Desc = "Failed to retrieve version info"
                });
            }
        }
    }
}
