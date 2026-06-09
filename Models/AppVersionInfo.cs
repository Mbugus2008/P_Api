using System.Text.Json.Serialization;

namespace ParcelAPI.Models
{
    public class AppVersionInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("versionCode")]
        public int VersionCode { get; set; }

        [JsonPropertyName("buildDate")]
        public string BuildDate { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("releaseNotes")]
        public string? ReleaseNotes { get; set; }

        [JsonPropertyName("forceUpdate")]
        public bool ForceUpdate { get; set; }
    }
}
