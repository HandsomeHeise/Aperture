using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Aperture.Transport;

public sealed record ProtocolResponse(int StatusCode, string Message, IReadOnlyList<string> Lines, byte[] Binary);

public sealed class ConsoleConnection : IAsyncDisposable
{
    private const int MaxPayload = 64 * 1024 * 1024;
    private readonly TcpClient client;
    private readonly ProtocolReader reader;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int closed;
    public string Host { get; }
    public int Port { get; }
    public TimeSpan Timeout { get; set; }
    public bool IsConnected => Volatile.Read(ref closed) == 0;
    private ConsoleConnection(TcpClient client, string host, int port, TimeSpan timeout)
    {
        this.client = client; Host = host; Port = port; Timeout = timeout;
        reader = new(client.GetStream());
    }
    public static async Task<ConsoleConnection> ConnectAsync(string host, int port = 730, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var duration = timeout ?? TimeSpan.FromSeconds(5);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(duration);
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            var connection = new ConsoleConnection(tcp, host, port, duration);
            var greeting = ParseStatus(await connection.reader.LineAsync(deadline.Token).ConfigureAwait(false));
            if (greeting.StatusCode != 201) throw new ApertureProtocolException(greeting.StatusCode, greeting.Message);
            return connection;
        }
        catch { tcp.Dispose(); throw; }
    }
    public Task<ProtocolResponse> ExecuteAsync(string command, CancellationToken cancellationToken = default) => ExchangeAsync(command, null, false, null, cancellationToken);
    public async Task<byte[]> ReceiveBinaryAsync(string command, int length, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > MaxPayload) throw new ArgumentOutOfRangeException(nameof(length));
        return (await ExchangeAsync(command, length, false, null, cancellationToken).ConfigureAwait(false)).Binary;
    }
    public async Task<byte[]> ReceiveLengthPrefixedAsync(string command, CancellationToken cancellationToken = default) => (await ExchangeAsync(command, null, true, null, cancellationToken).ConfigureAwait(false)).Binary;
    public Task<ProtocolResponse> SendBinaryAsync(string command, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => ExchangeAsync(command, null, false, data, cancellationToken);
    /// <summary>Reads the native GETMEMEX framing and returns the available prefix, which may be shorter than requested.</summary>
    public async Task<byte[]> ReceiveChunkedMemoryAsync(string command, int length, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (length > MaxPayload) throw new ArgumentOutOfRangeException(nameof(length));
        return (await ExchangeAsync(command, length, false, null, cancellationToken, true).ConfigureAwait(false)).Binary;
    }

    private async Task<ProtocolResponse> ExchangeAsync(string command, int? length, bool prefixed, ReadOnlyMemory<byte>? upload, CancellationToken ct, bool chunked = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 1024 * 1024 || command.Any(c => c is '\r' or '\n' or '\0' || c > 255)) throw new ArgumentException("Command contains invalid protocol characters or exceeds the limit.", nameof(command));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(!IsConnected, this);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.Latin1.GetBytes(command + "\r\n"), deadline.Token).ConfigureAwait(false);
            var status = ParseStatus(await reader.LineAsync(deadline.Token).ConfigureAwait(false));
            if (status.StatusCode >= 400) throw new ApertureProtocolException(status.StatusCode, status.Message);
            if (upload is { } payload)
            {
                if (status.StatusCode != 204) throw new InvalidDataException("Expected send-binary status 204.");
                await stream.WriteAsync(payload, deadline.Token).ConfigureAwait(false);
                status = ParseStatus(await reader.LineAsync(deadline.Token).ConfigureAwait(false));
                if (status.StatusCode >= 400) throw new ApertureProtocolException(status.StatusCode, status.Message);
                if (status.StatusCode != 200) throw new InvalidDataException("Expected upload completion status 200.");
            }
            var lines = new List<string>();
            var binary = Array.Empty<byte>();
            if (status.StatusCode == 202)
            {
                var total = 0;
                while (true)
                {
                    var line = await reader.LineAsync(deadline.Token).ConfigureAwait(false);
                    if (line == ".") break;
                    total = checked(total + line.Length);
                    if (total > MaxPayload) throw new InvalidDataException("Multiline response exceeds the limit.");
                    lines.Add(line);
                }
            }
            else if (status.StatusCode == 203)
            {
                var count = length;
                if (prefixed)
                {
                    var header = await reader.BytesAsync(4, deadline.Token).ConfigureAwait(false);
                    var advertised = BinaryPrimitives.ReadUInt32LittleEndian(header);
                    if (advertised > MaxPayload) throw new InvalidDataException("Binary response exceeds the limit.");
                    count = (int)advertised;
                }
                if (count is null) throw new InvalidDataException("Binary response requires an explicit framing length.");
                if (chunked)
                {
                    using var data = new MemoryStream();
                    while (data.Length < count.Value)
                    {
                        var header = BinaryPrimitives.ReadUInt16LittleEndian(await reader.BytesAsync(2, deadline.Token).ConfigureAwait(false));
                        var size = header & 0x7fff;
                        if (size > count.Value - data.Length || size == 0 && (header & 0x8000) == 0) throw new InvalidDataException("Invalid GETMEMEX chunk size.");
                        data.Write(await reader.BytesAsync(size, deadline.Token).ConfigureAwait(false));
                        if ((header & 0x8000) != 0) break;
                    }
                    binary = data.ToArray();
                }
                else binary = await reader.BytesAsync(count.Value, deadline.Token).ConfigureAwait(false);
            }
            else if (status.StatusCode is not (200 or 201 or 205)) throw new InvalidDataException("Unexpected protocol status " + status.StatusCode);
            if ((length is not null || prefixed) && status.StatusCode != 203) throw new InvalidDataException("Expected binary response status 203.");
            return new(status.StatusCode, status.Message, lines, binary);
        }
        catch (ApertureProtocolException) { throw; }
        catch { Close(); throw; }
        finally { gate.Release(); }
    }
    internal static ProtocolResponse ParseStatus(string line)
    {
        if (line.Length < 4 || line[3] != '-' || line.Length > 4 && line[4] != ' ' || !int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var code)) throw new InvalidDataException("Malformed debug-monitor status line.");
        return new(code, line.Length > 5 ? line[5..] : "", [], []);
    }
    private void Close() { if (Interlocked.Exchange(ref closed, 1) == 0) client.Dispose(); }
    public ValueTask DisposeAsync() { Close(); return ValueTask.CompletedTask; }
}
