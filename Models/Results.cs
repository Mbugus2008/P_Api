using System.Text.Json.Serialization;

namespace ParcelAPI.Models
{
    public class Results<T>
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("contents")]
        public T? Contents { get; set; }
    }
}
