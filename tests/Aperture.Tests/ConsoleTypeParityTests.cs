using Aperture.Models;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class ConsoleTypeParityTests
{
 [Theory]
 [InlineData("devkit",ConsoleType.DevelopmentKit)]
 [InlineData("testkit",ConsoleType.TestKit)]
 [InlineData("reviewerkit",ConsoleType.ReviewerKit)]
 public async Task ConsoleTypeUsesNativeNamedKinds(string response,ConsoleType expected)
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("consoletype",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- "+response+"\r\n",ct);});
  await using var console=await XboxConsole.ConnectAsync("127.0.0.1",server.Port);
  var getter=typeof(XboxConsole).GetMethod("GetConsoleTypeAsync");Assert.NotNull(getter);
  Assert.Equal(expected,await (Task<ConsoleType>)getter.Invoke(console,[CancellationToken.None])!);
 }
}
