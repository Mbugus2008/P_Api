namespace ParcelAPI.Models
{
    public class AppVersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public int VersionCode { get; set; }
        public string BuildDate { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
        public bool ForceUpdate { get; set; }
    }
}
