using ParcelAPI.Clients;
using ParcelAPI.Models;

namespace ParcelAPI.Services;

public class NavSmsService
{
    private readonly ILogger<NavSmsService> _logger;
    private readonly IClientFactory _clientFactory;

    public NavSmsService(ILogger<NavSmsService> logger, IClientFactory clientFactory)
    {
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public async Task<SmsResponse> SendBulkAsync(List<SmsRequest> messages, string clientId)
    {
        var response = new SmsResponse { Code = 0, Desc = "OK" };

        if (messages == null || messages.Count == 0)
        {
            response.Code = -1;
            response.Desc = "No messages to send";
            return response;
        }

        var client = await _clientFactory.GetClientAsync(clientId);
        if (client == null)
        {
            response.Code = -1;
            response.Desc = "Client not found";
            return response;
        }

        dynamic navClient = NavClientHelper.InitializeClientCodeunit<Posting.MBranch_PortClient>(client.ClientInfo);

        foreach (var msg in messages)
        {
            var result = new SmsResult { Phone = msg.Phone };
            try
            {
                // NAV SendSms expects: source, telephone, textsms, documentNo, dates
                await navClient.SendsmsAsync(
                    source: "PARCEL",
                    telephone: msg.Phone,
                    textsms: msg.Message,
                    documentNo: msg.DocumentNo ?? "",
                    dates: DateTime.Now
                );
                result.Success = true;
                _logger.LogInformation("SMS sent to {Phone}", msg.Phone);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                _logger.LogError(ex, "Failed to send SMS to {Phone}", msg.Phone);
            }
            response.Results.Add(result);
        }

        var failedCount = response.Results.Count(r => !r.Success);
        if (failedCount > 0)
        {
            response.Code = failedCount == messages.Count ? -1 : 1;
            response.Desc = $"{failedCount} of {messages.Count} messages failed";
        }

        return response;
    }
}
