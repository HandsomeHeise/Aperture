using Aperture.Consoles;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class SharedConsoleParityTests
{
 [Fact] public async Task SharedManagerLeasesKeepSocketAliveUntilFinalOwnerDisposes()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("ping",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- alive\r\n",ct);});
  var options=new ConsoleConnectOptions();var share=typeof(ConsoleConnectOptions).GetProperty("Shared");Assert.NotNull(share);share.SetValue(options,true);
  var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
  var first=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",options,server.Port);
  var second=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",options,server.Port);
  Assert.Same(first.Connection,second.Connection);
  await first.DisposeAsync();Assert.True(second.Connection.IsConnected);Assert.Equal("alive",(await second.SendCommandAsync("ping")).Message);
  await second.DisposeAsync();Assert.False(second.Connection.IsConnected);
 }
}
