using System.Net;
using Aperture.Automation;
using Aperture.Debugging;
using Aperture.Files;
using Aperture.Dumps;
using Aperture.Imaging;
using Aperture.Consoles;
using Aperture.Memory;
using Aperture.Rpc;
using Aperture.Transport;

namespace Aperture;

public sealed record RunningProcess(uint ProcessId, string ProgramName);

public sealed class XboxConsole : IAsyncDisposable
{
    public ConsoleManager? Manager { get; internal set; }
    public bool Shared => ReleaseSharedConnection is not null;
    internal Func<ValueTask>? ReleaseSharedConnection { get; set; }
    private int disposed;
    public string Name { get; private set; }
    public string Host => Connection.Host;
    public ConsoleConnection Connection { get; }
    public ConsoleMemory Memory { get; }
    public JrpcClient Rpc { get; }
    public ConsoleFileSystem Files { get; }
    public DebugTarget DebugTarget { get; }
    public ControllerClient Automation { get; }
    public DumpControl Dumps { get; }
    public UserAccessClient Users { get; }
    internal XboxConsole(ConsoleConnection connection, string name, ConsoleMemory? sharedMemory = null)
    {
        Connection = connection; Name = name; DebugTarget = new(connection, sharedMemory, this); Memory = DebugTarget.Memory; Rpc = new(connection); Files = new(connection); Automation = new(connection); Dumps = new(connection); Users = new(connection);
    }
    public static async Task<XboxConsole> ConnectAsync(string host, int port = 730, CancellationToken cancellationToken = default)
        => await ConnectWithOptionsAsync(host, new(), port, cancellationToken).ConfigureAwait(false);
    public static async Task<XboxConsole> ConnectWithOptionsAsync(string host, ConsoleConnectOptions options, int port = 730, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options); options.Validate();
        if (options.Shared) throw new ArgumentException("Shared connections must be opened through ConsoleManager.OpenConsoleWithOptionsAsync.", nameof(options));
        for (var attempt = 0; ; attempt++)
        {
            ConsoleConnection? connection = null;
            try
            {
                connection = await ConsoleConnection.ConnectAsync(host, port, options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
                connection.Timeout = options.ConversationTimeout;
                var name = (await connection.ExecuteAsync("dbgname", cancellationToken).ConfigureAwait(false)).Message;
                return new(connection, name);
            }
            catch (Exception error) when (attempt + 1 < options.Attempts && !cancellationToken.IsCancellationRequested && error is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
            {
                if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); throw; }
        }
    }
    public Task<ProtocolResponse> SendCommandAsync(string command, CancellationToken cancellationToken = default) => Connection.ExecuteAsync(command, cancellationToken);
    public Task<ScreenshotFrame> CaptureScreenshotAsync(CancellationToken cancellationToken = default) => ScreenshotFrame.CaptureAsync(Host, Connection.Port, cancellationToken);
    public async Task SetNameAsync(string name, CancellationToken cancellationToken = default)
    {
        _ = await SendCommandAsync("dbgname name=" + ProtocolFields.Quote(name), cancellationToken).ConfigureAwait(false); Name = name;
    }
    public async Task<IPAddress> GetIPAddressAsync(CancellationToken cancellationToken = default)
    {
        if (IPAddress.TryParse(Host, out var address)) return address;
        return (await Dns.GetHostAddressesAsync(Host, cancellationToken).ConfigureAwait(false)).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }
    public async Task<IPAddress> GetTitleIPAddressAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("altaddr", cancellationToken).ConfigureAwait(false);
        var address = ProtocolFields.Parse(response.Message).GetUInt32("addr");
        return new IPAddress(new byte[] { (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address });
    }
    public async Task<IReadOnlyDictionary<string, string>> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync("systeminfo", cancellationToken).ConfigureAwait(false);
        return ProtocolFields.Parse(string.Join(' ', response.Lines));
    }
    public async Task<Aperture.Models.ConsoleType> GetConsoleTypeAsync(CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await SendCommandAsync("consoletype", cancellationToken).ConfigureAwait(false)).Message);
        return fields.ContainsKey("devkit") ? Aperture.Models.ConsoleType.DevelopmentKit
            : fields.ContainsKey("testkit") ? Aperture.Models.ConsoleType.TestKit
            : fields.ContainsKey("reviewerkit") ? Aperture.Models.ConsoleType.ReviewerKit
            : throw new InvalidDataException("The monitor returned an unknown console type.");
    }
    public async Task<RunningProcess> GetRunningProcessAsync(CancellationToken cancellationToken = default)
    {
        var pid = ProtocolFields.Parse((await SendCommandAsync("getpid", cancellationToken).ConfigureAwait(false)).Message).GetUInt32("pid");
        var executable = await SendCommandAsync("xbeinfo running", cancellationToken).ConfigureAwait(false);
        var fields = ProtocolFields.Parse(executable.StatusCode == 202 ? string.Join(' ', executable.Lines) : executable.Message);
        return new(pid, fields.GetString("name"));
    }
    public async Task<DateTimeOffset> GetSystemTimeAsync(CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await SendCommandAsync("systime", cancellationToken).ConfigureAwait(false)).Message);
        return DateTimeOffset.FromFileTime(checked((long)(((ulong)fields.GetUInt32("high") << 32) | fields.GetUInt32("low"))));
    }
    public async Task SetSystemTimeAsync(DateTimeOffset time, CancellationToken cancellationToken = default)
    {
        var value = unchecked((ulong)time.ToFileTime());
        _ = await SendCommandAsync(FormattableString.Invariant($"setsystime clockhi=0x{value >> 32:X8} clocklo=0x{(uint)value:X8}"), cancellationToken).ConfigureAwait(false);
    }
    public async Task SetDefaultTitleAsync(string title, string? directory = null, string? commandLine = null, bool persist = true, CancellationToken cancellationToken = default)
    {
        var command = "title name=" + ProtocolFields.Quote(title);
        if (directory is not null) command += " dir=" + ProtocolFields.Quote(directory);
        if (commandLine is not null) command += " cmdline=" + ProtocolFields.Quote(commandLine);
        if (persist) command += " persist";
        _ = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Uses native DMTITLE flags: bit 0 persists; bit 1 permits a null title to clear persistence.</summary>
    public async Task SetDefaultTitleAsync(string? title, string? directory, uint flags, CancellationToken cancellationToken = default)
    {
        if ((flags & ~3u) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        if (title is null)
        {
            if ((flags & 2) == 0) throw new ArgumentNullException(nameof(title));
            await ClearDefaultTitleAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        // DmSetTitleEx RVA 0x471BC clears persistence before the final TITLE command,
        // and deliberately ignores the monitor result of this preliminary command.
        try { await ClearDefaultTitleAsync(cancellationToken).ConfigureAwait(false); }
        catch (ApertureProtocolException) { }
        await SetDefaultTitleAsync(title, directory, commandLine: null, persist: (flags & 1) != 0, cancellationToken).ConfigureAwait(false);
    }
    public async Task ClearDefaultTitleAsync(CancellationToken cancellationToken = default) => _ = await SendCommandAsync("title nopersist", cancellationToken).ConfigureAwait(false);
    /// <summary>Reboots using the XDevkit flag values. Both historical warm and cold flags request a cold boot on current monitors.</summary>
    public async Task RebootAsync(string? title, string? directory, string? commandLine, Aperture.Models.RebootFlags flags, CancellationToken cancellationToken = default)
    {
        if (((int)flags & ~15) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        var command = "magicboot";
        if (title is not null)
        {
            command += " title=" + ProtocolFields.Quote(title);
            if (directory is not null) command += " directory=" + ProtocolFields.Quote(directory);
            if (commandLine is not null) command += " cmdline=" + ProtocolFields.Quote(commandLine);
        }
        if ((flags & (Aperture.Models.RebootFlags.Cold | Aperture.Models.RebootFlags.Warm)) != 0) command += " cold";
        if ((flags & Aperture.Models.RebootFlags.Stop) != 0) command += " stop";
        else if ((flags & Aperture.Models.RebootFlags.Wait) != 0) command += " wait";
        try { _ = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false); }
        finally { await Connection.DisposeAsync().ConfigureAwait(false); await DisposeAsync().ConfigureAwait(false); }
    }
    public async Task RebootAsync(string? title = null, string? directory = null, bool cold = false, bool wait = false, bool stop = false, CancellationToken cancellationToken = default)
    {
        var command = "magicboot";
        if (title is not null) command += " title=" + ProtocolFields.Quote(title);
        if (directory is not null) command += " directory=" + ProtocolFields.Quote(directory);
        if (cold) command += " cold";
        if (stop) command += " stop"; else if (wait) command += " wait";
        try { _ = await SendCommandAsync(command, cancellationToken).ConfigureAwait(false); }
        finally { await Connection.DisposeAsync().ConfigureAwait(false); await DisposeAsync().ConfigureAwait(false); }
    }
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref disposed, 1) == 0 ? ReleaseSharedConnection?.Invoke() ?? Connection.DisposeAsync() : ValueTask.CompletedTask;
}

