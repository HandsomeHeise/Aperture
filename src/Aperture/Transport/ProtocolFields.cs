using System.Globalization;

namespace Aperture.Transport;

public static class ProtocolFields
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
            if (i == text.Length) break;
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('=' or ',')) i++;
            if (i == start) throw new FormatException("Malformed protocol field.");
            var name = text[start..i]; var value = "";
            if (i < text.Length && text[i] == '=')
            {
                i++;
                if (i < text.Length && text[i] == '"')
                {
                    start = ++i;
                    while (i < text.Length && text[i] != '"') i++;
                    if (i == text.Length) throw new FormatException("Unterminated quoted protocol value.");
                    value = text[start..i++];
                }
                else
                {
                    start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != ',') i++;
                    value = text[start..i];
                }
            }
            fields[name] = value;
        }
        return fields;
    }
    public static uint UInt32(string value) => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : value.StartsWith('-') ? unchecked((uint)int.Parse(value, CultureInfo.InvariantCulture)) : uint.Parse(value, CultureInfo.InvariantCulture);
    public static ulong UInt64(string value) => value.StartsWith("0q", StringComparison.OrdinalIgnoreCase) || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ulong.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : ulong.Parse(value, CultureInfo.InvariantCulture);
    public static uint GetUInt32(this IReadOnlyDictionary<string, string> fields, string key, uint fallback = 0) => fields.TryGetValue(key, out var value) ? UInt32(value) : fallback;
    public static string GetString(this IReadOnlyDictionary<string, string> fields, string key, string fallback = "") => fields.TryGetValue(key, out var value) ? value : fallback;
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(c => c is '"' or '\r' or '\n' or '\0' || c > 255)) throw new ArgumentException("Value cannot be represented safely in the debug-monitor protocol.", nameof(value));
        return '"' + value + '"';
    }
}
