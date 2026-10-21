using Aperture.Consoles;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class SharedRebootParityTests
{
 [Fact] public async Task RebootInvalidatesEveryLeaseOnTheSharedSocket()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("magicboot",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- OK\r\n",ct);});
  var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));var options=new ConsoleConnectOptions{Shared=true};await using var first=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",options,server.Port);await using var second=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",options,server.Port);await first.RebootAsync();Assert.False(second.Connection.IsConnected);
 }
}
