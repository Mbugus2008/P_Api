using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ParcelAPI.Clients;
using ParcelAPI.Models;
using ParcelAPI.Services;

namespace ParcelAPI.Filters
{
    /// <summary>
    /// Action filter that validates the X-Client-Identifier header and loads the client
    /// </summary>
    public class ClientIdentifierFilter : IAsyncActionFilter
    {
        private readonly IClientService _clientService;

        public ClientIdentifierFilter(IClientService clientService)
        {
            _clientService = clientService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            // Safaricom callbacks and C2B confirmations do not send X-Client-Identifier
            if (path.EndsWith("/callback", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/c2b", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var clientId = context.HttpContext.Request.Headers["X-Client-Identifier"].ToString();
            
            if (string.IsNullOrEmpty(clientId))
            {
                context.Result = new BadRequestObjectResult(new Results<object>
                {
                    Code = -1,
                    Desc = "Missing X-Client-Identifier header"
                });
                return;
            }

            // Store client ID in HttpContext.Items
            context.HttpContext.Items["ClientId"] = clientId;

            // Load and store the client
            var client = await _clientService.GetIClientAsync(clientId);
            if (client == null)
            {
                context.Result = new BadRequestObjectResult(new Results<object>
                {
                    Code = -1,
                    Desc = $"Client '{clientId}' not found"
                });
                return;
            }

            context.HttpContext.Items["Client"] = client;

            await next();
        }
    }
}
