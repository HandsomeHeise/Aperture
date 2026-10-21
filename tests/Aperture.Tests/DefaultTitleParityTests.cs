using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class DefaultTitleParityTests
{
 [Theory]
 [InlineData(0u,"title name=\"default.xex\" dir=\"Hdd:\\game\"")]
 [InlineData(1u,"title name=\"default.xex\" dir=\"Hdd:\\game\" persist")]
 public async Task SettingNativeDefaultTitleFirstClearsPreviousPersistence(uint flags,string expected)
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("title nopersist",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"400- nothing to clear\r\n",ct);Assert.Equal(expected,await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- OK\r\n",ct);});await using var console=await XboxConsole.ConnectAsync("127.0.0.1",server.Port);var method=typeof(XboxConsole).GetMethods().FirstOrDefault(m=>m.Name=="SetDefaultTitleAsync"&&m.GetParameters().Any(p=>p.ParameterType==typeof(uint)));Assert.NotNull(method);await (Task)method.Invoke(console,["default.xex",@"Hdd:\game",flags,CancellationToken.None])!;
 }
}
