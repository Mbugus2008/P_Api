using System.ComponentModel.DataAnnotations.Schema;

namespace ParcelAPI.Models
{
    public class Client
    {
        public int Id { get; set; }
        
        [Column("Client Code")]
        public string ClientCode { get; set; } = string.Empty;
        
        [Column("Client Name")]
        public string? ClientName { get; set; }
        
        public bool Active { get; set; }
        public string? Email { get; set; }
        public string? Contact { get; set; }
        public string? Company { get; set; }
        
        // NAV Connection Settings
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? Instance { get; set; }
        public int? Port { get; set; }
        public string? IPAddress { get; set; }
        
        [Column("Log Path")]
        public string? LogPath { get; set; }
    }
}