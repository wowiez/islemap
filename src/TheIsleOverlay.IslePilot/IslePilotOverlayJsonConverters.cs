using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheIsleOverlay.IslePilot;

public sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var intVal)) return intVal;
                if (reader.TryGetInt64(out var longVal)) return (int)longVal;
                if (reader.TryGetDouble(out var dVal)) return (int)Math.Round(dVal);
                return 0;
            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str)) return 0;
                if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt)) return parsedInt;
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble)) return (int)Math.Round(parsedDouble);
                return 0;
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            case JsonTokenType.Null:
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}

public sealed class FlexibleNullableIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var intVal)) return intVal;
                if (reader.TryGetInt64(out var longVal)) return (int)longVal;
                if (reader.TryGetDouble(out var dVal)) return (int)Math.Round(dVal);
                return 0;
            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str)) return null;
                if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt)) return parsedInt;
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble)) return (int)Math.Round(parsedDouble);
                return null;
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

public sealed class FlexibleDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str)) return 0d;
                return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) ? val : 0d;
            case JsonTokenType.Null:
                return 0d;
            default:
                return 0d;
        }
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}

public sealed class FlexibleNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str)) return null;
                return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) ? val : null;
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

public sealed class FlexibleDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str)) return null;
                if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                    return dto;
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var num))
                {
                    return num > 100_000_000_000L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(num)
                        : DateTimeOffset.FromUnixTimeSeconds(num);
                }
                return null;
            case JsonTokenType.Null:
                return null;
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value.ToString("O"));
        else writer.WriteNullValue();
    }
}

public sealed class SafeGlitchLabConverter : JsonConverter<IslePilotOverlaySkinGlitchLabDto>
{
    public override IslePilotOverlaySkinGlitchLabDto? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            double? pi = null;
            double? sv = null;
            var layers = new Dictionary<string, IslePilotOverlaySkinGlitchLayerDto>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.TryGetProperty("pi", out var piProp) && TryGetDouble(piProp, out var piVal))
            {
                pi = piVal;
            }

            if (doc.RootElement.TryGetProperty("sv", out var svProp) && TryGetDouble(svProp, out var svVal))
            {
                sv = svVal;
            }

            if (doc.RootElement.TryGetProperty("layers", out var layersProp) && layersProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in layersProp.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        double? a = null, x = null, y = null, z = null;
                        if (prop.Value.TryGetProperty("a", out var aProp) && TryGetDouble(aProp, out var aVal)) a = aVal;
                        if (prop.Value.TryGetProperty("x", out var xProp) && TryGetDouble(xProp, out var xVal)) x = xVal;
                        if (prop.Value.TryGetProperty("y", out var yProp) && TryGetDouble(yProp, out var yVal)) y = yVal;
                        if (prop.Value.TryGetProperty("z", out var zProp) && TryGetDouble(zProp, out var zVal)) z = zVal;
                        layers[prop.Name] = new IslePilotOverlaySkinGlitchLayerDto { A = a, X = x, Y = y, Z = z };
                    }
                }
            }

            return new IslePilotOverlaySkinGlitchLabDto
            {
                Pi = pi,
                Sv = sv,
                Layers = layers
            };
        }
        catch
        {
            return new IslePilotOverlaySkinGlitchLabDto();
        }
    }

    private static bool TryGetDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    public override void Write(
        Utf8JsonWriter writer, IslePilotOverlaySkinGlitchLabDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Pi.HasValue) writer.WriteNumber("pi", value.Pi.Value);
        if (value.Sv.HasValue) writer.WriteNumber("sv", value.Sv.Value);
        if (value.Layers != null)
        {
            writer.WriteStartObject("layers");
            foreach (var (key, layer) in value.Layers)
            {
                writer.WriteStartObject(key);
                if (layer.A.HasValue) writer.WriteNumber("a", layer.A.Value);
                if (layer.X.HasValue) writer.WriteNumber("x", layer.X.Value);
                if (layer.Y.HasValue) writer.WriteNumber("y", layer.Y.Value);
                if (layer.Z.HasValue) writer.WriteNumber("z", layer.Z.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}

public sealed class ResilientSkinDraftListConverter : JsonConverter<IReadOnlyList<IslePilotOverlaySkinDraftDto>>
{
    public override IReadOnlyList<IslePilotOverlaySkinDraftDto> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return Array.Empty<IslePilotOverlaySkinDraftDto>();
        }

        var list = new List<IslePilotOverlaySkinDraftDto>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            try
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var draft = doc.Deserialize<IslePilotOverlaySkinDraftDto>(options);
                if (draft != null)
                {
                    list.Add(draft);
                }
            }
            catch
            {
                // Gracefully skip corrupted or unparseable single draft items so the rest still load
            }
        }

        return list;
    }

    public override void Write(
        Utf8JsonWriter writer, IReadOnlyList<IslePilotOverlaySkinDraftDto> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
    }
}
