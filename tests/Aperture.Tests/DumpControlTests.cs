using Aperture.Dumps;
using Aperture.Models;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;
public sealed class DumpControlTests
{
    [Fact]
    public async Task DeferFlagsAndSettingsRetainNativeMeanings()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("eventdefer", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- break debugstring ntassert\r\n", ct);
            Assert.Equal("dumpsettings", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- rpt=never dst=local fmt=noheap path=\"HDD:\\dumps\"\r\n", ct);
            Assert.Equal("eventdefer set singlestep rip", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- OK\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var control = new DumpControl(connection);
        Assert.Equal(EventDeferFlags.CanDeferExecutionBreak | EventDeferFlags.CanDeferDebugString | EventDeferFlags.CanDeferAssertionFailedEx, await control.GetEventDeferFlagsAsync());
        Assert.Equal(DumpReportFlags.NeverReport | DumpReportFlags.FormatNoHeap, (await control.GetSettingsAsync()).Flags);
        await control.SetEventDeferFlagsAsync(EventDeferFlags.CanDeferSingleStep | EventDeferFlags.CanDeferRIP);
    }
}
