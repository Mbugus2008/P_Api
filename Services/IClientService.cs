using ParcelAPI.Clients;
using ParcelAPI.Models;
using NavUsers = User;

namespace ParcelAPI.Services
{
    public interface IClientService
    {
        Task<NavUsers.Parcel_Users[]> GetParcelUsersAsync(string clientId);
        Task<Client?> GetClientAsync(string clientCode);
        Task<IClient?> GetIClientAsync(string clientCode);
    }
}