using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core;

// The profile is shared with the web version, so reading is forgiving: numbers may come as strings and ids as numbers.
public static class Json
{
    public static readonly JsonSerializerOptions Opts = Make(false);
    public static readonly JsonSerializerOptions Pretty = Make(true);

    static JsonSerializerOptions Make(bool indent)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            WriteIndented = indent,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new LenientString());
        o.Converters.Add(new LenientDouble());
        o.Converters.Add(new LenientLong());
        o.Converters.Add(new LenientInt());
        o.Converters.Add(new LenientBool());
        return o;
    }

    public static T? Parse<T>(string s) => JsonSerializer.Deserialize<T>(s, Opts);
    public static string Str<T>(T v) => JsonSerializer.Serialize(v, Opts);
}

sealed class LenientString : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.String: return r.GetString();
            case JsonTokenType.Number:
                using (var d = JsonDocument.ParseValue(ref r)) return d.RootElement.GetRawText();
            case JsonTokenType.True: return "true";
            case JsonTokenType.False: return "false";
            case JsonTokenType.Null: return null;
            default: r.Skip(); return null;
        }
    }
    public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
}

sealed class LenientDouble : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.Number: return r.GetDouble();
            case JsonTokenType.String:
                return double.TryParse(r.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            case JsonTokenType.True: return 1;
            case JsonTokenType.False: case JsonTokenType.Null: return 0;
            default: r.Skip(); return 0;
        }
    }
    public override void Write(Utf8JsonWriter w, double v, JsonSerializerOptions o)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
        if (v == Math.Floor(v) && Math.Abs(v) < 9e15) w.WriteNumberValue((long)v);
        else w.WriteNumberValue(Math.Round(v, 4));
    }
}

sealed class LenientLong : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.Number: return r.TryGetInt64(out var l) ? l : (long)r.GetDouble();
            case JsonTokenType.String:
                return double.TryParse(r.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (long)v : 0;
            case JsonTokenType.True: return 1;
            case JsonTokenType.False: case JsonTokenType.Null: return 0;
            default: r.Skip(); return 0;
        }
    }
    public override void Write(Utf8JsonWriter w, long v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}

sealed class LenientInt : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.Number: return r.TryGetInt32(out var l) ? l : (int)r.GetDouble();
            case JsonTokenType.String:
                return double.TryParse(r.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (int)v : 0;
            case JsonTokenType.True: return 1;
            case JsonTokenType.False: case JsonTokenType.Null: return 0;
            default: r.Skip(); return 0;
        }
    }
    public override void Write(Utf8JsonWriter w, int v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}

sealed class LenientBool : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: case JsonTokenType.Null: return false;
            case JsonTokenType.Number: return r.GetDouble() != 0;
            case JsonTokenType.String:
                var s = (r.GetString() ?? "").Trim().ToLowerInvariant();
                return s is not ("" or "false" or "0" or "no");
            default: r.Skip(); return true; // e.g. an artwork object means "has artwork"
        }
    }
    public override void Write(Utf8JsonWriter w, bool v, JsonSerializerOptions o) => w.WriteBooleanValue(v);
}
