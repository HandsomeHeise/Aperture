using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Aperture.Models;
using Aperture.Transport;

namespace Aperture.Debugging;

public sealed record DebugNotification(DebugEventType Type, string Source, string Text, string Raw, IReadOnlyDictionary<string, string> Fields)
{
    public ReadOnlyMemory<byte> Payload => System.Text.Encoding.Latin1.GetBytes(Text);
    public DebugEventInfo EventInfo => DebugEventInfo.FromNotification(this);
    public uint ThreadId => Fields.GetUInt32("thread");
    public uint Address => Fields.GetUInt32("addr", Fields.GetUInt32("address"));
    public static DebugNotification Parse(string line)
    {
        var separator = line.IndexOfAny([' ', '!']);
        var source = separator < 0 ? line : line[..separator];
        var body = separator < 0 ? "" : line[(separator + 1)..];
        // Custom monitor extensions use source!text framing; their payload has no protocol-field grammar.
        if (separator >= 0 && line[separator] == '!')
            return new(DebugEventType.NoEvent, source, body, line, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>()));
        var stringStart = body.IndexOf("string=", StringComparison.OrdinalIgnoreCase);
        var fields = ProtocolFields.Parse(stringStart >= 0 ? body[..stringStart] : body);
        var type = source.ToLowerInvariant() switch
        {
            "break" => DebugEventType.ExecutionBreak, "debugstr" => DebugEventType.DebugString,
            "execution" => DebugEventType.ExecStateChange, "singlestep" => DebugEventType.SingleStep,
            "modload" => DebugEventType.ModuleLoad, "modunload" => DebugEventType.ModuleUnload,
            "create" => DebugEventType.ThreadCreate, "terminate" => DebugEventType.ThreadDestroy,
            "exception" => DebugEventType.Exception, "assert" => DebugEventType.AssertionFailed,
            "ntassert" or "assertex" => DebugEventType.AssertionFailedEx, "data" or "databreak" => DebugEventType.DataBreak,
            "rip" => DebugEventType.RIP, "sectload" => DebugEventType.SectionLoad, "sectunload" => DebugEventType.SectionUnload,
            "stacktrace" => DebugEventType.StackTrace, "fiber" => fields.ContainsKey("delete") ? DebugEventType.FiberDestroy : DebugEventType.FiberCreate,
            "pogomodstartup" => DebugEventType.PgoModuleStartup,
            _ => DebugEventType.NoEvent
        };
        var text = stringStart >= 0 ? body[(stringStart + 7)..] : body;
        if (type == DebugEventType.DebugString) text += fields.ContainsKey("cr") ? "\r" : fields.ContainsKey("lf") ? "\n" : fields.ContainsKey("crlf") ? "\r\n" : "";
        return new(type, source, text, line, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase)));
    }
}

/// <summary>Owns the monitor's reverse TCP notification subscription. Dispose to unregister.</summary>
public sealed class NotificationSession : IAsyncDisposable
{
    private readonly ConsoleConnection connection;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<DebugNotification> events = Channel.CreateBounded<DebugNotification>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true });
    private readonly Task worker;
    private TcpClient? reverse;
    private int disposed;
    public int Port { get; }
    private NotificationSession(ConsoleConnection connection, IPAddress address)
    {
        this.connection = connection; listener = new(address, 0); listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        worker = ReadAsync();
    }
    public static async Task<NotificationSession> SubscribeAsync(ConsoleConnection connection, IPAddress? listenAddress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var session = new NotificationSession(connection, listenAddress ?? IPAddress.Any);
        try
        {
            _ = await connection.ExecuteAsync(FormattableString.Invariant($"notifyat port={session.Port}"), cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            session.stop.Cancel(); session.listener.Stop();
            await session.worker.ConfigureAwait(false); session.stop.Dispose(); throw;
        }
    }
    public IAsyncEnumerable<DebugNotification> ReadAllAsync(CancellationToken cancellationToken = default) => events.Reader.ReadAllAsync(cancellationToken);
    private async Task ReadAsync()
    {
        Exception? failure = null;
        try
        {
            var allowed = await Dns.GetHostAddressesAsync(connection.Host, stop.Token).ConfigureAwait(false);
            while (!stop.IsCancellationRequested)
            {
                var candidate = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                var remote = ((IPEndPoint)candidate.Client.RemoteEndPoint!).Address;
                if (!allowed.Any(a => a.Equals(remote) || a.MapToIPv6().Equals(remote.MapToIPv6()))) { candidate.Dispose(); continue; }
                reverse = candidate; break;
            }
            if (reverse is null) return;
            var reader = new ProtocolReader(reverse.GetStream());
            while (!stop.IsCancellationRequested)
            {
                var line = await reader.LineAsync(stop.Token).ConfigureAwait(false);
                if (line.Length > 0) await events.Writer.WriteAsync(DebugNotification.Parse(line), stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; }
        finally { reverse?.Dispose(); events.Writer.TryComplete(failure); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel(); listener.Stop(); reverse?.Dispose();
        await worker.ConfigureAwait(false);
        try
        {
            if (connection.IsConnected) _ = await connection.ExecuteAsync(FormattableString.Invariant($"notifyat port={Port} drop")).ConfigureAwait(false);
        }
        finally { stop.Dispose(); }
    }
}


