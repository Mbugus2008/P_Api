using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParcelAPI.Data;

namespace ParcelAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class HealthController : ControllerBase
    {
        private readonly ParcelContext _context;
        private readonly ILogger<HealthController> _logger;
        private readonly IWebHostEnvironment _env;

        public HealthController(
            ParcelContext context,
            ILogger<HealthController> logger,
            IWebHostEnvironment env)
        {
            _context = context;
            _logger = logger;
            _env = env;
        }

        /// <summary>
        /// Lightweight health check — no DB call.
        /// Use this as the primary monitor; the Android app retries if this returns non-200.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public IActionResult GetHealth()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                environment = _env.EnvironmentName,
                version = GetAppVersion()
            });
        }

        /// <summary>
        /// Deep health check — verifies DB connectivity.
        /// Run this during deployment to confirm the new code + schema are compatible.
        /// </summary>
        [HttpGet("ready")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> GetReadiness()
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    return StatusCode(503, new { status = "unhealthy", reason = "database unreachable" });
                }

                // Optional: run a simple query to verify schema is ready
                // await _context.Database.ExecuteSqlRawAsync("SELECT 1");

                return Ok(new
                {
                    status = "ready",
                    database = "connected",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return StatusCode(503, new { status = "unhealthy", reason = ex.Message });
            }
        }

        private static string GetAppVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            return assembly.GetName()?.Version?.ToString() ?? "0.0.0.0";
        }
    }
}
