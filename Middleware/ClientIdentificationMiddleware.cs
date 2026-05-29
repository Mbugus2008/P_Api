namespace ParcelAPI.Middleware
{
    public class ClientIdentificationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ClientIdentificationMiddleware> _logger;

        public ClientIdentificationMiddleware(
            RequestDelegate next,
            ILogger<ClientIdentificationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientId = context.Request.Headers["X-Client-Identifier"].ToString();

            if (!string.IsNullOrEmpty(clientId))
            {
                context.Items["X-Client-Identifier"] = clientId;
                _logger.LogInformation("Processing request for client: {ClientId}", clientId);
            }
            else
            {
                _logger.LogWarning("Request missing X-Client-Identifier header from {IP}", 
                    context.Connection.RemoteIpAddress);
            }

            await _next(context);
        }
    }
}