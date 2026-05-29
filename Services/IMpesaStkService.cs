using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface IMpesaStkService
    {
        Task<MpesaStkResponse> InitiateStkPushAsync(MpesaStkRequest request, string callbackUrl);
        Task HandleCallbackAsync(MpesaStkCallback callback);
        Task<MpesaStkStatus?> GetStatusAsync(string checkoutRequestId);
    }
}
