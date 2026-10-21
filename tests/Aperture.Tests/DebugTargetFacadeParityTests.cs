using Aperture.Consoles;
using Aperture.Debugging;
using Aperture.Tests.Fixtures;
using Aperture.Transport;
using Xunit;
namespace Aperture.Tests;
public sealed class DebugTargetFacadeParityTests
{
 [Fact] public async Task OwnedDebugTargetRetainsConsoleAndManagerIdentity()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);});var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));await using var console=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",new(),server.Port);
  var owner=typeof(DebugTarget).GetProperty("Console");Assert.NotNull(owner);Assert.Same(console,owner.GetValue(console.DebugTarget));Assert.Same(manager,typeof(DebugTarget).GetProperty("Manager")!.GetValue(console.DebugTarget));Assert.Equal("kit",typeof(DebugTarget).GetProperty("Name")!.GetValue(console.DebugTarget));
 }
 [Theory]
 [InlineData("stop",426,"StopWithStatusAsync")]
 [InlineData("go",408,"GoWithStatusAsync")]
 public async Task ExecutionStatusPreservesNativeAlreadyStoppedAndNotStopped(string command,int status,string name)
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal(command,await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,status+"- state unchanged\r\n",ct);});await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);var target=new DebugTarget(connection);var method=typeof(DebugTarget).GetMethod(name);Assert.NotNull(method);Assert.True(await (Task<bool>)method.Invoke(target,[CancellationToken.None])!);
 }
}
