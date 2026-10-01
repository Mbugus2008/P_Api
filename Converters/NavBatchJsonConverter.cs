using System.Text.Json;
using System.Text.Json.Serialization;
using P_Batches;

namespace ParcelAPI.Converters;

/// <summary>
/// Accepts the legacy mobile-app spellings of the batch timestamps
/// ("Received_DateTime" / "Dispatch_DateTime") in addition to the NAV field
/// names ("Received_Date_Time" / "Dispatch_Date_Time").
///
/// Devices still running app versions 1.0.37-1.0.43 send the legacy names;
/// without this alias their dispatch/receive times were silently dropped and
/// NAV stored NULL for both timestamps.
/// </summary>
public class NavBatchJsonConverter : JsonConverter<ParcelBatches>
{
    // Used for the rewritten document. Deliberately does NOT contain this
    // converter, so deserialization cannot recurse.
    private static readonly JsonSerializerOptions InnerOptions = new()
    {
        Converters =
        {
            new JsonStringEnumConverter(),
            new NullableDateTimeConverter()
        }
    };

    public override ParcelBatches? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var name = property.Name switch
                {
                    "Received_DateTime" => "Received_Date_Time",
                    "Dispatch_DateTime" => "Dispatch_Date_Time",
                    _ => property.Name
                };

                // The same logical value may arrive under both spellings;
                // keep the first one seen for each target name.
                if (!written.Add(name)) continue;

                writer.WritePropertyName(name);
                property.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        stream.Position = 0;
        return (ParcelBatches?)JsonSerializer.Deserialize(stream, typeof(ParcelBatches), InnerOptions);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ParcelBatches value,
        JsonSerializerOptions options)
    {
        // Responses are serialized with the normal NAV field names.
        var clone = new JsonSerializerOptions(InnerOptions);
        JsonSerializer.Serialize(writer, value, clone);
    }
}
