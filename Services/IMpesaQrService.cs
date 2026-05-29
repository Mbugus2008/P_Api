using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface IMpesaQrService
    {
        Task<MpesaQrResponse> GenerateQrCodeAsync(MpesaQrRequest request);
    }
}
