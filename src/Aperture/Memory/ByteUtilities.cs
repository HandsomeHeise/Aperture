using System.Text;

namespace Aperture.Memory;

public static class ByteUtilities
{
    public static string ToHexString(string value) => Convert.ToHexString(Latin1(value));
    public static byte[] ToByteArray(string value) => [.. Latin1(value), 0];
    public static byte[] ToWchar(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.BigEndianUnicode.GetBytes(value + '\0');
    }
    public static byte[] Append(ReadOnlySpan<byte> bytes, byte value) => [.. bytes, value];
    public static int Find(string value, string needle)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(needle);
        return needle.Length == 0 ? -1 : value.IndexOf(needle, StringComparison.Ordinal);
    }
    private static byte[] Latin1(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(c => c > 255)) throw new ArgumentException("String is outside Latin-1. Use ToWchar for Unicode.", nameof(value));
        return Encoding.Latin1.GetBytes(value);
    }
}
