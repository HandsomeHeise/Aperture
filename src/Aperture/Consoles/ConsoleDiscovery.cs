using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Aperture.Consoles;

public sealed record DiscoveredConsole(string Name, IPAddress Address);
public static class ConsoleDiscovery
{
    public static async Task<DiscoveredConsole> FindAsync(string name, ConsoleConnectOptions? options = null, TimeSpan? discoveryDuration = null, IPEndPoint? destination = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        options ??= new(); options.Validate();
        var isAddress = IPAddress.TryParse(name, out var address);
        for (var attempt = 0; attempt < options.Attempts; attempt++)
        {
            var consoles = await DiscoverAsync(discoveryDuration, destination, cancellationToken).ConfigureAwait(false);
            var found = consoles.FirstOrDefault(c => isAddress ? c.Address.Equals(address) : string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
            if (attempt + 1 < options.Attempts) await Task.Delay(options.RetryDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new IOException($"Console '{name}' did not respond to discovery.");
    }
    /// <summary>Broadcasts the native one-byte discovery request; a destination can select a subnet or host.</summary>
    public static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverAsync(TimeSpan? duration = null, IPEndPoint? destination = null, CancellationToken cancellationToken = default)
    {
        var timeout = duration ?? TimeSpan.FromSeconds(3);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (destination is not null && destination.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("XBDM discovery uses IPv4.", nameof(destination));
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout);
        await socket.SendAsync(new byte[] { 0x83 }, destination ?? new IPEndPoint(IPAddress.Broadcast, 730), deadline.Token).ConfigureAwait(false);
        var found = new Dictionary<IPAddress, DiscoveredConsole>();
        try
        {
            while (true)
            {
                var packet = await socket.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                var bytes = packet.Buffer;
                if (bytes.Length < 3 || bytes[0] != 2 || bytes[1] == 0 || bytes[1] == 255 || bytes[1] > bytes.Length - 2) continue;
                var name = Encoding.Latin1.GetString(bytes, 2, bytes[1]);
                if (name.Any(c => char.IsControl(c))) continue;
                found[packet.RemoteEndPoint.Address] = new(name, packet.RemoteEndPoint.Address);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        cancellationToken.ThrowIfCancellationRequested();
        return found.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
