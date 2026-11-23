using System.Text.Json;
using System.Text.Json.Serialization;

namespace WahSpices.API.Json;

/// <summary>
/// The app stores timestamps as UTC (DateTime.UtcNow), but SQL Server hands them back with
/// Kind = Unspecified, so they were serialised without a "Z" and browsers read them as local
/// time (5h30m off in India). This writes every DateTime as explicit UTC ("...Z").
/// Reading is unchanged. Applies to DateTime? as well.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc);
    }
}
