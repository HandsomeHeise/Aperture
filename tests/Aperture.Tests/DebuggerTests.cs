using Aperture.Debugging;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;

public sealed class DebuggerTests
{
    [Fact]
    public async Task ContextUsesNativeVrKeywordAndBreakpointTypesUseNativeValues()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("getcontext thread=0xFB000008 control int fp vr", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "202- context\r\nIar=0x82000000 Gpr1=0q000000007A010000\r\n.\r\n", ct);
            Assert.Equal("isbreak addr=0x82000000", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- type=4\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var debug = new DebugTarget(connection);
        Assert.Equal(0x82000000u, (await debug.GetRegistersAsync(0xFB000008)).Get32(Aperture.Models.Register32.iar));
        Assert.Equal(Aperture.Models.BreakpointType.OnExecute, await debug.GetBreakpointTypeAsync(0x82000000));
    }
    [Fact]
    public async Task ModulesRetainPDataAndOriginalSize()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "202- modules\r\nname=\"xam.xex\" base=0x815f0000 size=0x00610000 check=0x004a9c52 timestamp=0x5daa92a1 pdata=0x81653c00 psize=0x0001af38 dllthread=0 osize=0x005a0200\r\n.\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var module = Assert.Single(await new DebugTarget(connection).GetModulesAsync());
        Assert.Equal(0x81653c00u, module.PDataAddress); Assert.Equal(0x005a0200u, module.OriginalSize);
        Assert.Equal("xam.xex", module.Name);
    }
    [Fact]
    public async Task ThreadListAcceptsSignedDecimalIdsFromRetailMonitor()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("threads", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "202- threads\r\n-83886072\r\n0xFB000018\r\n.\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(new uint[] { 0xFB000008, 0xFB000018 }, await new DebugTarget(connection).GetThreadIdsAsync());
    }
}
