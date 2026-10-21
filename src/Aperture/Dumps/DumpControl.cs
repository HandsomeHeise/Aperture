using Aperture.Models;
using Aperture.Transport;

namespace Aperture.Dumps;

public sealed record DumpSettings(DumpReportFlags Flags, string NetworkPath);
public sealed class DumpControl(ConsoleConnection connection)
{
    private static readonly (EventDeferFlags Flag, string Name)[] defer =
    [
        (EventDeferFlags.CanDeferExecutionBreak, "break"), (EventDeferFlags.CanDeferDebugString, "debugstring"),
        (EventDeferFlags.CanDeferSingleStep, "singlestep"), (EventDeferFlags.CanDeferAssertionFailed, "assert"),
        (EventDeferFlags.CanDeferAssertionFailedEx, "ntassert"), (EventDeferFlags.CanDeferDataBreak, "databreak"), (EventDeferFlags.CanDeferRIP, "rip")
    ];
    public async Task<DumpMode> GetModeAsync(CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await connection.ExecuteAsync("dumpmode", cancellationToken).ConfigureAwait(false)).Message);
        if (fields.ContainsKey("smart")) return DumpMode.Smart;
        if (fields.ContainsKey("enabled")) return DumpMode.Enabled;
        if (fields.ContainsKey("disabled")) return DumpMode.Disabled;
        throw new InvalidDataException("The monitor returned no recognized dump mode.");
    }
    public async Task SetModeAsync(DumpMode mode, CancellationToken cancellationToken = default)
    {
        var name = mode switch { DumpMode.Smart => "smart", DumpMode.Enabled => "enabled", DumpMode.Disabled => "disabled", _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        _ = await connection.ExecuteAsync("dumpmode " + name, cancellationToken).ConfigureAwait(false);
    }
    public async Task<EventDeferFlags> GetEventDeferFlagsAsync(CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await connection.ExecuteAsync("eventdefer", cancellationToken).ConfigureAwait(false)).Message);
        return defer.Where(f => fields.ContainsKey(f.Name)).Aggregate((EventDeferFlags)0, (result, f) => result | f.Flag);
    }
    public async Task SetEventDeferFlagsAsync(EventDeferFlags flags, CancellationToken cancellationToken = default)
    {
        if (((int)flags & ~127) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        var names = defer.Where(f => flags.HasFlag(f.Flag)).Select(f => f.Name).ToArray();
        _ = await connection.ExecuteAsync("eventdefer set" + (names.Length > 0 ? " " + string.Join(' ', names) : ""), cancellationToken).ConfigureAwait(false);
    }
    public async Task<DumpSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("dumpsettings", cancellationToken).ConfigureAwait(false);
        var fields = ProtocolFields.Parse(response.StatusCode == 202 ? string.Join(' ', response.Lines) : response.Message);
        var report = fields.GetString("rpt") switch { "prompt" => 0, "always" => 1, "never" => 2, _ => throw new InvalidDataException("Unknown dump report setting.") };
        var destination = fields.GetString("dst") switch { "local" => 0, "remote" => 16, _ => throw new InvalidDataException("Unknown dump destination.") };
        var format = fields.GetString("fmt") switch { "full" => 0, "partial" => 256, "noheap" => 512, "retail" => 1024, _ => throw new InvalidDataException("Unknown dump format.") };
        return new((DumpReportFlags)(report | destination | format), fields.GetString("path"));
    }
    public async Task SetSettingsAsync(DumpSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var flags = (int)settings.Flags;
        if ((flags & ~0xf1f) != 0) throw new ArgumentOutOfRangeException(nameof(settings));
        var report = (flags & 15) switch { 0 => "prompt", 1 => "always", 2 => "never", _ => throw new ArgumentOutOfRangeException(nameof(settings)) };
        var destination = (flags & 0xf0) switch { 0 => "local", 16 => "remote", _ => throw new ArgumentOutOfRangeException(nameof(settings)) };
        var format = (flags & 0xf00) switch { 0 => "full", 256 => "partial", 512 => "noheap", 1024 => "retail", _ => throw new ArgumentOutOfRangeException(nameof(settings)) };
        _ = await connection.ExecuteAsync($"dumpsettings rpt={report} dst={destination} fmt={format} path={ProtocolFields.Quote(settings.NetworkPath)}", cancellationToken).ConfigureAwait(false);
    }
    public async Task CaptureOnConsoleAsync(bool stopAfter = false, bool waitAfter = false, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("crashdump" + (stopAfter ? " stop" : waitAfter ? " wait" : ""), cancellationToken).ConfigureAwait(false);
}
