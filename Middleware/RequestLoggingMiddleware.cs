using System.Diagnostics;
using System.Text;

namespace ParcelAPI.Middleware;

/// <summary>
/// Logs every HTTP request and response: method, path, client, status code, duration, body.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var request = context.Request;

        // Log the request
        var clientId = request.Headers["X-Client-Identifier"].ToString();
        var requestBody = string.Empty;
        if (request.ContentLength > 0 && request.ContentLength < 100_000)
        {
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            request.Body.Position = 0;
        }

        _logger.LogInformation(
            "▶ REQUEST {Method} {Path}{Query} | Client={Client} | ContentLength={Length} | Body={Body}",
            request.Method,
            request.Path,
            request.QueryString,
            clientId.Length > 0 ? clientId : "(none)",
            request.ContentLength ?? 0,
            Truncate(requestBody, 500));

        // Capture the response
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                "✖ ERROR {Method} {Path} | Status=500 | Duration={Duration}ms | {Error}",
                request.Method, request.Path, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }

        sw.Stop();

        // Read response
        responseBody.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(responseBody).ReadToEndAsync();
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBodyStream);

        var level = context.Response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information;
        _logger.Log(level,
            "◀ RESPONSE {Method} {Path} | Status={Status} | Duration={Duration}ms | Length={Length} | Body={Body}",
            request.Method,
            request.Path,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds,
            responseText.Length,
            Truncate(responseText, 500));
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) ? "(empty)"
        : value.Length <= maxLength ? value
        : value[..maxLength] + $"... [truncated, total {value.Length} chars]";
}
