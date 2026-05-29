using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParcelAPI.Converters;

/// <summary>
/// Custom JSON converter that handles null values for DateTime fields.
/// When null is encountered, it returns DateTime.MinValue instead of throwing an error.
/// </summary>
public class NullableDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return DateTime.MinValue;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrEmpty(stringValue))
            {
                return DateTime.MinValue;
            }

            if (DateTime.TryParse(stringValue, out var dateTime))
            {
                return dateTime;
            }
        }

        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        if (value == DateTime.MinValue)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
