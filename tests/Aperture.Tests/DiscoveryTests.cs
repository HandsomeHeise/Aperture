using System.Net;
using System.Net.Sockets;
using Aperture.Consoles;
using Xunit;

namespace Aperture.Tests;
public sealed class DiscoveryTests
{
    [Fact]
    public async Task NativeDiscoveryUses83AndRejectsTruncatedNames()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)server.Client.LocalEndPoint!;
        var script = Task.Run(async () =>
        {
            var request = await server.ReceiveAsync(); Assert.Equal(new byte[] { 0x83 }, request.Buffer);
            await server.SendAsync(new byte[] { 2, 9, 65 }, request.RemoteEndPoint);
            await server.SendAsync(new byte[] { 2, 4, 74, 116, 97, 103 }, request.RemoteEndPoint);
        });
        var consoles = await ConsoleDiscovery.DiscoverAsync(TimeSpan.FromMilliseconds(200), endpoint);
        await script;
        var console = Assert.Single(consoles); Assert.Equal("Jtag", console.Name); Assert.Equal(IPAddress.Loopback, console.Address);
    }
}
