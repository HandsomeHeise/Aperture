using Aperture.Debugging;
using Aperture.Models;
using Xunit;
namespace Aperture.Tests;
public sealed class EventInfoTests
{
    [Fact]
    public void FiberPreservesNativeSlotsEvenThoughNativeParameterCountIsZero()
    {
        var e = DebugNotification.Parse("fiber id=0x42 start=0x82001000").EventInfo;
        Assert.Equal(0u, e.ParameterCount);
        Assert.Equal(15, e.ParameterSlots.Count);
        Assert.Equal(0x42u, e.ParameterSlots[0]);
        Assert.Equal(0x82001000u, e.ParameterSlots[1]);
    }
    [Fact]
    public void SnapshotOwnsExceptionParameters()
    {
        var values = new uint[] { 1, 2 };
        var original = DebugNotification.Parse("break thread=9").EventInfo with { Parameters = values };
        var snapshot = original.Snapshot();
        values[0] = 99;
        Assert.Equal(1u, snapshot.Parameters[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)snapshot.Parameters)[0] = 3);
    }
    [Fact]
    public void CustomNotificationsPreserveUnstructuredText()
    {
        var notification = DebugNotification.Parse("plugin!hello = world");
        Assert.Equal(DebugEventType.NoEvent, notification.Type);
        Assert.Equal("plugin", notification.Source);
        Assert.Equal("hello = world", notification.Text);
        Assert.Empty(notification.Fields);
    }
    [Fact]
    public void ExceptionProjectsStopFlagsAndAddress()
    {
        var e = DebugNotification.Parse("exception thread=0x42 code=0xC0000005 address=0x82001000 first noncont stop read=0xDEAD").EventInfo;
        Assert.True(e.IsThreadStopped);
        Assert.Equal(0x42u, e.ThreadId);
        Assert.Equal(0xC0000005u, e.Code);
        Assert.Equal(0x82001000u, e.Address);
        Assert.Equal(ExceptionFlags.FirstChance | ExceptionFlags.Noncontinuable, e.Flags);
    }
    [Fact]
    public void UnresolvedExceptionParametersCannotBeSilentlyDropped()
    {
        Assert.Throws<InvalidOperationException>(() => DebugNotification.Parse("exception code=1 nparam=2 params=0x82000000").EventInfo);
    }
    [Fact]
    public async Task ExceptionParametersResolveFromBigEndianConsoleMemory()
    {
        await using var server = new Aperture.Tests.Fixtures.DebugMonitorServer(async (stream, ct) =>
        {
            Assert.Equal("getmem addr=0x82000000 length=0x8", await Aperture.Tests.Fixtures.DebugMonitorServer.ReadLine(stream, ct));
            await Aperture.Tests.Fixtures.DebugMonitorServer.Write(stream, "202- memory\r\n0000000182001234\r\n.\r\n", ct);
        });
        await using var connection = await Aperture.Transport.ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var e = await DebugNotification.Parse("exception code=1 nparam=2 params=0x82000000").GetEventInfoAsync(new DebugTarget(connection));
        Assert.Equal(new uint[] { 1, 0x82001234 }, e.Parameters);
        Assert.Equal(2u, e.ParameterCount);
    }
    [Fact]
    public void DebugStringRestoresNativeLineEndingAndPreservesBytes()
    {
        var e = DebugNotification.Parse("debugstr thread=1 crlf string=hiÿ");
        Assert.Equal("hiÿ\r\n", e.Text);
        Assert.Equal(new byte[] { 104, 105, 255, 13, 10 }, e.Payload.ToArray());
        Assert.False(e.EventInfo.IsThreadStopped);
    }
    [Fact]
    public void NativeStopReasonsUseDataAndNtassertNames()
    {
        Assert.Equal(DebugEventType.DataBreak, DebugNotification.Parse("data addr=1 thread=2 write=3 stop").Type);
        Assert.Equal(DebugEventType.AssertionFailedEx, DebugNotification.Parse("ntassert addr=1 thread=2 stop").Type);
    }
    [Fact]
    public async Task ThreadStopEventProjectsNativeReason()
    {
        await using var server = new Aperture.Tests.Fixtures.DebugMonitorServer(async (stream, ct) =>
        {
            Assert.Equal("isstopped thread=0x00000042", await Aperture.Tests.Fixtures.DebugMonitorServer.ReadLine(stream, ct));
            await Aperture.Tests.Fixtures.DebugMonitorServer.Write(stream, "200- break addr=0x82001000\r\n", ct);
        });
        await using var connection = await Aperture.Transport.ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var e = await new DebugTarget(connection).GetStopEventInfoAsync(0x42);
        Assert.True(e.IsThreadStopped);
        Assert.Equal(0x42u, e.ThreadId);
        Assert.Equal(DebugEventType.ExecutionBreak, e.Event);
        Assert.Equal(0x82001000u, e.Address);
    }
    [Fact]
    public async Task AssertionResolvesItsExactLengthConsolePayload()
    {
        await using var server = new Aperture.Tests.Fixtures.DebugMonitorServer(async (stream, ct) =>
        {
            Assert.Equal("getmem addr=0x82000000 length=0x3", await Aperture.Tests.Fixtures.DebugMonitorServer.ReadLine(stream, ct));
            await Aperture.Tests.Fixtures.DebugMonitorServer.Write(stream, "202- memory\r\n41FF42\r\n.\r\n", ct);
        });
        await using var connection = await Aperture.Transport.ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var e = await DebugNotification.Parse("assert thread=1 straddr=0x82000000 strlen=3 stop").GetEventInfoAsync(new DebugTarget(connection));
        Assert.Equal("AÿB", e.Message);
    }
    [Fact]
    public void DataBreakProjectsBreakKindAndDataAddress()
    {
        var e = DebugNotification.Parse("data addr=0x82001000 thread=1 write=0xDEAD stop").EventInfo;
        Assert.Equal(new uint[] { 1, 0xDEAD }, e.Parameters);
    }
    [Fact]
    public void AccessViolationNparamAndParamsAreScalarInformationRatherThanPointer()
    {
        var e = DebugNotification.Parse("exception code=0xC0000005 nparam=1 params=0xDEAD").EventInfo;
        Assert.Equal(new uint[] { 1, 0xDEAD }, e.Parameters);
    }
    [Fact]
    public void PgoModuleStartupProjectsItsModuleHandle()
    {
        var e = DebugNotification.Parse("pogomodstartup name=\"game.xex\" module=0x123 thread=1").EventInfo;
        Assert.Equal(new uint[] { 0x123 }, e.Parameters);
    }
    [Theory]
    [InlineData("read", 0u)]
    [InlineData("write", 1u)]
    public void LegacyAccessViolationInformationProjectsItsTwoNativeValues(string access, uint kind)
    {
        var e = DebugNotification.Parse("exception code=0xC0000005 " + access + "=0xDEAD").EventInfo;
        Assert.Equal(new uint[] { kind, 0xDEAD }, e.Parameters);
    }
    [Theory]
    [InlineData("started", ExecutionState.Running)]
    [InlineData("stopped", ExecutionState.Stopped)]
    [InlineData("pending_title", ExecutionState.PendingTitle)]
    [InlineData("reboot_title", ExecutionState.RebootingTitle)]
    public void ExecutionProjectsState(string token, ExecutionState state)
    {
        Assert.Equal(state, DebugNotification.Parse("execution " + token).EventInfo.ExecState);
    }
    [Fact]
    public void ModuleAndSectionEventsProjectTheirDescriptors()
    {
        var m = DebugNotification.Parse("modload name=\"game.xex\" base=0x82000000 size=0x1000 checksum=0x123 timestamp=0x456 pdata=0x82100000 psize=0x100 xbe tls").EventInfo.Module;
        Assert.NotNull(m);
        Assert.Equal(0x82000000u, m.BaseAddress);
        Assert.Equal("game.xex", m.Name);
        Assert.Equal(0x123u, m.Checksum);
        Assert.Equal((ModuleInfoFlags)3, m.Flags);
        var s = DebugNotification.Parse("sectload name=\".text\" base=0x82001000 size=0x200 index=1 flags=9").EventInfo.Section;
        Assert.NotNull(s);
        Assert.Equal(9u, s.Flags);
    }
}










