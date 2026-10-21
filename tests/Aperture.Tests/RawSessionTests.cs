using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;
public sealed class RawSessionTests
{
    [Fact]
    public async Task DedicatedConnectionExposesLinesAndBinaryWithoutAutomaticConsumption()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("dedicate handler=custom", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "205- dedicated\r\n", ct);
            Assert.Equal("hello", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "203- data\r\nlength=3\r\n", ct);
            await s.WriteAsync(new byte[] { 13, 10, 255 }, ct);
        });
        await using var session = await MonitorSession.ConnectAsync("127.0.0.1", server.Port, "custom");
        Assert.Equal(203, (await session.SendTextCommandAsync("hello")).StatusCode);
        Assert.Equal("length=3", await session.ReceiveLineAsync());
        Assert.Equal(new byte[] { 13, 10, 255 }, await session.ReceiveBinaryAsync(3));
    }
}
