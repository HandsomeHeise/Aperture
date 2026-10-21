using System.Net.Sockets;
using System.Text;

namespace Aperture.Transport;

/// <summary>A caller-controlled protocol conversation. Each operation consumes only the requested framing.</summary>
/// <remarks>Keep an entire multi-operation conversation on one caller. Independent callers should open separate sessions.</remarks>
public sealed class MonitorSession : IAsyncDisposable
{
    private readonly TcpClient client;
    private readonly ProtocolReader reader;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int closed;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    private MonitorSession(TcpClient client) { this.client = client; reader = new(client.GetStream()); }
    public static async Task<MonitorSession> ConnectAsync(string host, int port = 730, string? handler = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (handler is not null && (handler.Length == 0 || handler.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))) throw new ArgumentException("Handler must be a protocol token.", nameof(handler));
        var tcp = new TcpClient { NoDelay = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await tcp.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            var session = new MonitorSession(tcp);
            var greeting = await session.ReceiveStatusAsync(deadline.Token).ConfigureAwait(false);
            if (greeting.StatusCode != 201) throw new InvalidDataException("Expected debug monitor greeting.");
            if (handler is not null)
            {
                var status = await session.SendTextCommandAsync("dedicate handler=" + handler, deadline.Token).ConfigureAwait(false);
                if (status.StatusCode != 205) throw new InvalidDataException("The connection was not dedicated.");
            }
            return session;
        }
        catch { tcp.Dispose(); throw; }
    }
    public Task<ProtocolResponse> SendTextCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        Validate(command);
        return Operation(async ct =>
        {
            await client.GetStream().WriteAsync(Encoding.Latin1.GetBytes(command + "\r\n"), ct).ConfigureAwait(false);
            return Status(await reader.LineAsync(ct).ConfigureAwait(false));
        }, cancellationToken);
    }
    public Task<string> ReceiveLineAsync(CancellationToken cancellationToken = default) => Operation(ct => reader.LineAsync(ct), cancellationToken);
    public Task<ProtocolResponse> ReceiveStatusAsync(CancellationToken cancellationToken = default) => Operation(async ct => Status(await reader.LineAsync(ct).ConfigureAwait(false)), cancellationToken);
    public Task<byte[]> ReceiveBinaryAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count < 0 || count > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(count));
        return Operation(ct => reader.BytesAsync(count, ct), cancellationToken);
    }
    public async Task SendBinaryAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        _ = await Operation(async ct => { await client.GetStream().WriteAsync(bytes, ct).ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);
    }
    private async Task<T> Operation<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(Timeout);
        await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(closed != 0, this); return await operation(deadline.Token).ConfigureAwait(false); }
        catch (ApertureProtocolException) { throw; }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
        finally { gate.Release(); }
    }
    private static ProtocolResponse Status(string line)
    {
        var status = ConsoleConnection.ParseStatus(line);
        if (status.StatusCode >= 400) throw new ApertureProtocolException(status.StatusCode, status.Message);
        return status;
    }
    private static void Validate(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 1024 * 1024 || command.Any(c => c is '\r' or '\n' or '\0' || c > 255)) throw new ArgumentException("Invalid protocol command.", nameof(command));
    }
    public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref closed, 1) == 0) client.Dispose(); return ValueTask.CompletedTask; }
}
