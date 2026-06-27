using Microsoft.AspNetCore.Mvc;

namespace ParcelAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ErrorsController : ControllerBase
{
    private readonly ILogger<ErrorsController> _logger;

    public ErrorsController(ILogger<ErrorsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Receives error reports from the mobile app.
    /// </summary>
    [HttpPost("report")]
    [Consumes("application/json")]
    public IActionResult ReportError([FromBody] AppErrorReport report)
    {
        _logger.LogError(
            "📱 APP ERROR | Device={DeviceId} | User={User} | Version={Version} | Location={Location}\n" +
            "   Error: {Error}\n   StackTrace: {StackTrace}",
            report.DeviceId ?? "?",
            report.User ?? "?",
            report.AppVersion ?? "?",
            report.Location ?? "?",
            report.Error ?? "(no message)",
            report.StackTrace ?? "(no stack trace)");

        return Ok(new { received = true });
    }
}

public class AppErrorReport
{
    public string? DeviceId { get; set; }
    public string? User { get; set; }
    public string? Location { get; set; }
    public string? AppVersion { get; set; }
    public string? Error { get; set; }
    public string? StackTrace { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
