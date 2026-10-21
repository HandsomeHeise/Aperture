using System.Globalization;
using System.Text;
using Aperture.Transport;

namespace Aperture.Memory;

public sealed class ConsoleMemory(ConsoleConnection connection)
{
    private readonly object cacheLock = new();
    private readonly Dictionary<(uint Address, int Length), byte[]> cache = [];
    private bool cacheEnabled;
    private long generation;
    private int cachedBytes;
    public bool CacheEnabled
    {
        get { lock (cacheLock) return cacheEnabled; }
        set { lock (cacheLock) { cacheEnabled = value; cache.Clear(); cachedBytes = 0; generation++; } }
    }
    public void InvalidateCache(uint address = 0, ulong length = 0x1_0000_0000UL)
    {
        if (length > 0x1_0000_0000UL - address) throw new ArgumentOutOfRangeException(nameof(length));
        lock (cacheLock)
        {
            generation++;
            foreach (var key in cache.Keys.Where(k => (ulong)k.Address < (ulong)address + length && (ulong)address < (ulong)k.Address + (uint)k.Length).ToArray())
            { cachedBytes -= cache[key].Length; cache.Remove(key); }
        }
    }
    public async Task<byte[]> ReadBinaryAsync(uint address, int length, CancellationToken cancellationToken = default)
    {
        ValidateRange(address, length);
        if (length == 0) return [];
        var bytes = await connection.ReceiveChunkedMemoryAsync(FormattableString.Invariant($"getmemex addr=0x{address:X8} length=0x{length:X}"), length, cancellationToken).ConfigureAwait(false);
        if (bytes.Length != length) throw new EndOfStreamException($"The console returned {bytes.Length} of {length} requested memory bytes.");
        return bytes;
    }
    public async Task<byte[]> ReadAsync(uint address, int length, CancellationToken cancellationToken = default)
    {
        ValidateRange(address, length);
        cancellationToken.ThrowIfCancellationRequested();
        long before;
        lock (cacheLock)
        {
            before = generation;
            if (cacheEnabled && cache.TryGetValue((address, length), out var existing)) return (byte[])existing.Clone();
        }
        var result = new byte[length]; var offset = 0;
        while (offset < length)
        {
            var count = Math.Min(1024, length - offset);
            var response = await connection.ExecuteAsync(FormattableString.Invariant($"getmem addr=0x{address + (uint)offset:X8} length=0x{count:X}"), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != 202) throw new InvalidDataException("Expected a multiline memory response.");
            var hex = string.Concat(response.Lines).Replace(" ", "", StringComparison.Ordinal);
            if (hex.Contains('?')) throw new IOException("The console reported unreadable memory.");
            var data = Convert.FromHexString(hex);
            if (data.Length != count) throw new EndOfStreamException("Memory response was shorter than requested.");
            data.CopyTo(result, offset); offset += count;
        }
        lock (cacheLock)
        {
            if (cacheEnabled && before == generation && length <= 16 * 1024 * 1024)
            {
                if (cachedBytes + length > 16 * 1024 * 1024) { cache.Clear(); cachedBytes = 0; }
                if (cache.TryGetValue((address, length), out var previous)) cachedBytes -= previous.Length;
                cache[(address, length)] = (byte[])result.Clone(); cachedBytes += length;
            }
        }
        return result;
    }
    public async Task WriteAsync(uint address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ValidateRange(address, data.Length);
        InvalidateCache(address, (uint)data.Length);
        var offset = 0;
        while (offset < data.Length)
        {
            var count = Math.Min(240, data.Length - offset);
            ProtocolResponse response;
            try { response = await connection.ExecuteAsync(FormattableString.Invariant($"setmem addr=0x{address + (uint)offset:X8} data={Convert.ToHexString(data.Span.Slice(offset, count))}"), cancellationToken).ConfigureAwait(false); }
            finally { InvalidateCache(address, (uint)data.Length); }
            var fields = ProtocolFields.Parse(response.Message);
            var written = fields.TryGetValue("set", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : throw new InvalidDataException("Memory write response has no transfer count.");
            if (written != count) throw new IOException("Memory write completed only partially.");
            offset += count;
        }
    }
    public async Task<T> ReadAsync<T>(uint address, CancellationToken cancellationToken = default) where T : struct => BigEndianCodec.Decode<T>(await ReadAsync(address, BigEndianCodec.SizeOf<T>(), cancellationToken).ConfigureAwait(false));
    public async Task<T[]> ReadArrayAsync<T>(uint address, int count, CancellationToken cancellationToken = default) where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return BigEndianCodec.DecodeArray<T>(await ReadAsync(address, checked(count * BigEndianCodec.SizeOf<T>()), cancellationToken).ConfigureAwait(false));
    }
    public Task WriteAsync<T>(uint address, T value, CancellationToken cancellationToken = default) where T : struct => WriteAsync(address, BigEndianCodec.Encode(value), cancellationToken);
    public Task WriteArrayAsync<T>(uint address, ReadOnlyMemory<T> values, CancellationToken cancellationToken = default) where T : struct => WriteAsync(address, BigEndianCodec.EncodeArray(values.Span), cancellationToken);
    public async Task<string> ReadStringAsync(uint address, int byteLength, Encoding? encoding = null, bool stopAtNull = true, CancellationToken cancellationToken = default)
    {
        var data = await ReadAsync(address, byteLength, cancellationToken).ConfigureAwait(false);
        var value = (encoding ?? Encoding.UTF8).GetString(data);
        var terminator = stopAtNull ? value.IndexOf('\0') : -1;
        return terminator < 0 ? value : value[..terminator];
    }
    public Task WriteStringAsync(uint address, string value, Encoding? encoding = null, bool nullTerminate = true, CancellationToken cancellationToken = default) => WriteAsync(address, (encoding ?? Encoding.UTF8).GetBytes(value + (nullTerminate ? "\0" : "")), cancellationToken);
    private static void ValidateRange(uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > 64 * 1024 * 1024 || (ulong)address + (uint)length > 0x1_0000_0000UL) throw new ArgumentOutOfRangeException(nameof(length), "Memory range exceeds limits or wraps the 32-bit address space.");
    }
}
