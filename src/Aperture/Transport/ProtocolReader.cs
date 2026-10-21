using System.Text;

namespace Aperture.Transport;

internal sealed class ProtocolReader(Stream stream)
{
    private readonly byte[] buffer = new byte[8192];
    private int position, count;
    private async ValueTask Fill(CancellationToken ct)
    {
        if (position < count) return;
        count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        position = 0;
        if (count == 0) throw new EndOfStreamException("The console closed the connection.");
    }
    public async Task<string> LineAsync(CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            await Fill(ct).ConfigureAwait(false);
            var newline = Array.IndexOf(buffer, (byte)10, position, count - position);
            var take = newline < 0 ? count - position : newline - position + 1;
            bytes.Write(buffer, position, take); position += take;
            if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Protocol line exceeds the limit.");
            if (newline < 0) continue;
            var data = bytes.ToArray();
            if (data.Length < 2 || data[^2] != 13) throw new InvalidDataException("Expected CRLF line terminator.");
            return Encoding.Latin1.GetString(data, 0, data.Length - 2);
        }
    }
    public async Task<byte[]> BytesAsync(int length, CancellationToken ct)
    {
        var bytes = new byte[length]; var offset = 0;
        while (offset < length)
        {
            await Fill(ct).ConfigureAwait(false);
            var take = Math.Min(length - offset, count - position);
            buffer.AsSpan(position, take).CopyTo(bytes.AsSpan(offset)); position += take; offset += take;
        }
        return bytes;
    }
}
