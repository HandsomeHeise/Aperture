using System.Text.Json;
using Microsoft.Win32;

namespace Aperture.Consoles;

public sealed record ConsoleSettings(string? DefaultConsole, string[] Consoles);

public sealed class ConsoleManager
{
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly Dictionary<(string Host, int Port, TimeSpan Timeout), SharedEntry> sharedConnections = new();
    private sealed class SharedEntry(XboxConsole console)
    {
        internal readonly XboxConsole Console = console;
        internal int Leases;
    }
    private readonly string settingsPath;
    private ConsoleSettings settings;
    public IReadOnlyList<string> Consoles => Array.AsReadOnly(settings.Consoles);
    public string? DefaultConsole => settings.DefaultConsole;
    public ConsoleManager(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aperture", "consoles.json");
        settings = File.Exists(this.settingsPath) ? JsonSerializer.Deserialize<ConsoleSettings>(File.ReadAllText(this.settingsPath)) ?? throw new InvalidDataException("Invalid console configuration.") : new(null, []);
        if (settingsPath is null && settings.DefaultConsole is null && OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\XenonSDK");
            if (key?.GetValue("XboxName") is string host && !string.IsNullOrWhiteSpace(host)) settings = new(host, [host]);
        }
    }
    public void AddConsole(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!settings.Consoles.Contains(host, StringComparer.OrdinalIgnoreCase)) settings = settings with { Consoles = [.. settings.Consoles, host] };
        Save();
    }
    public void RemoveConsole(string host)
    {
        settings = new(string.Equals(settings.DefaultConsole, host, StringComparison.OrdinalIgnoreCase) ? null : settings.DefaultConsole, settings.Consoles.Where(c => !string.Equals(c, host, StringComparison.OrdinalIgnoreCase)).ToArray()); Save();
    }
    public void SetDefaultConsole(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!settings.Consoles.Contains(host, StringComparer.OrdinalIgnoreCase)) settings = settings with { Consoles = [.. settings.Consoles, host] };
        settings = settings with { DefaultConsole = host }; Save();
    }
    public Task<XboxConsole> OpenConsoleAsync(string? host = null, CancellationToken cancellationToken = default) => OpenConsoleWithOptionsAsync(host, new(), cancellationToken: cancellationToken);
    public async Task<XboxConsole> OpenConsoleWithOptionsAsync(string? host, ConsoleConnectOptions options, int port = 730, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options); options.Validate();
        host ??= settings.DefaultConsole ?? throw new InvalidOperationException("No default console is configured.");
        if (options.Shared)
        {
            var key = (host.ToUpperInvariant(), port, options.ConversationTimeout);
            await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!sharedConnections.TryGetValue(key, out var entry) || !entry.Console.Connection.IsConnected)
                {
                    entry = new(await XboxConsole.ConnectWithOptionsAsync(host, options with { Shared = false }, port, cancellationToken).ConfigureAwait(false));
                    sharedConnections[key] = entry;
                }
                entry.Leases++;
                var lease = new XboxConsole(entry.Console.Connection, entry.Console.Name, entry.Console.Memory) { Manager = this };
                lease.ReleaseSharedConnection = async () =>
                {
                    await connectionGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (--entry.Leases == 0)
                        {
                            if (sharedConnections.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) sharedConnections.Remove(key);
                            await entry.Console.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally { connectionGate.Release(); }
                };
                return lease;
            }
            finally { connectionGate.Release(); }
        }
        var console = await XboxConsole.ConnectWithOptionsAsync(host, options, port, cancellationToken).ConfigureAwait(false);
        console.Manager = this;
        return console;
    }
    public Aperture.Dumps.DumpReader OpenDumpFile(string path) => new(path);
    public Aperture.Dumps.DumpReader OpenDumpFile(string path, string imageSearchPath) => new(path, imageSearchPath);
    public Task<Aperture.Dumps.DumpReader> OpenDumpFileAsync(string path, string imageSearchPath, CancellationToken cancellationToken = default)
        => Aperture.Dumps.DumpReader.OpenAsync(path, imageSearchPath, cancellationToken);
    /// <summary>Opens the managed browser wizard without requiring the SDK's desktop shell extension.</summary>
    public ConsoleDesktopSession RunAddConsoleWizard(Action<Uri>? launchBrowser = null) => ConsoleDesktopSession.Wizard(this, launchBrowser);
    public ConsoleDesktopSession RunConsoleSelection(Action<Uri>? launchBrowser = null) => ConsoleDesktopSession.Select(Consoles, launchBrowser);
    /// <summary>Opens an SDK-independent browser for the remote folder, navigation, and downloads.</summary>
    public async Task<ConsoleDesktopSession> OpenWindowsExplorerAsync(string? host = null, string path = "Hdd:\\", Action<Uri>? launchBrowser = null, ConsoleConnectOptions? options = null, int port = 730, CancellationToken cancellationToken = default)
    {
        var console = await OpenConsoleWithOptionsAsync(host, options ?? new(), port, cancellationToken).ConfigureAwait(false);
        try { return ConsoleDesktopSession.Explorer(console, path, launchBrowser); }
        catch { await console.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public string SystemSymbolServerPath => Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH") ?? "";
    public Task<IReadOnlyList<DiscoveredConsole>> DiscoverAsync(CancellationToken cancellationToken = default) => ConsoleDiscovery.DiscoverAsync(cancellationToken: cancellationToken);
    public string SelectConsole(Func<IReadOnlyList<string>, string>? selection = null)
    {
        if (selection is not null)
        {
            var selected = selection(Consoles);
            if (!settings.Consoles.Contains(selected, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Selected console is not in the configured list.");
            return selected;
        }
        return settings.DefaultConsole ?? (settings.Consoles.Length == 1 ? settings.Consoles[0] : throw new InvalidOperationException("Provide a selection callback when several consoles are configured."));
    }
    public async Task<string> SelectConsoleAsync(Aperture.Models.AccessFlags desiredAccess = 0, Aperture.Models.SelectConsoleFlags flags = Aperture.Models.SelectConsoleFlags.NoPromptIfDefaultExists, Func<IReadOnlyList<string>, CancellationToken, Task<string>>? selection = null, ConsoleConnectOptions? options = null, int port = 730, CancellationToken cancellationToken = default)
    {
        if (((int)desiredAccess & ~31) != 0) throw new ArgumentOutOfRangeException(nameof(desiredAccess));
        if (((int)flags & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        var candidates = new List<string>();
        foreach (var host in Consoles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (flags.HasFlag(Aperture.Models.SelectConsoleFlags.FilterByAccess))
            {
                try
                {
                    await using var console = await OpenConsoleWithOptionsAsync(host, options ?? new(), port, cancellationToken).ConfigureAwait(false);
                    var access = await console.Users.GetPrivilegesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    if ((access & (UserPrivileges)desiredAccess) != (UserPrivileges)desiredAccess) continue;
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is IOException or System.Net.Sockets.SocketException or OperationCanceledException) { continue; }
            }
            candidates.Add(host);
        }
        if (candidates.Count == 0) throw new InvalidOperationException("No configured console satisfies the selection criteria.");
        if (flags.HasFlag(Aperture.Models.SelectConsoleFlags.NoPromptIfOnlyOne))
        {
            if (candidates.Count == 1) return candidates[0];
        }
        else if (settings.DefaultConsole is { } configuredDefault && candidates.Contains(configuredDefault, StringComparer.OrdinalIgnoreCase)) return configuredDefault;
        if (selection is null)
        {
            await using var dialog = ConsoleDesktopSession.Select(candidates.AsReadOnly(), null);
            return await dialog.SelectionResult.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        var selected = await selection(candidates.AsReadOnly(), cancellationToken).ConfigureAwait(false);
        if (!candidates.Contains(selected, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Selected console is not in the eligible list.", nameof(selection));
        return selected;
    }
    public static string TranslateError(int hresult)
    {
        if ((unchecked((uint)hresult) & 0xFFFF0000u) == 0x82DA0000u) return Aperture.Transport.DebugMonitorErrors.Describe((int)(unchecked((uint)hresult) & 0xFFFFu));
        return System.Runtime.InteropServices.Marshal.GetExceptionForHR(hresult)?.Message ?? "Success";
    }
    private void Save()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".aperture-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, settingsPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
