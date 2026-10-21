using Aperture.Debugging;
using Aperture.Models;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class StopOnParityTests
{
 [Fact] public async Task StopOnPreservesNativePgoStartupTokenAndIgnoresUnimplementedTitleFlag()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("stopon pogomodstart",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- OK\r\n",ct);});await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);await new DebugTarget(connection).StopOnAsync(StopOnFlags.OnPgoModuleStartup|StopOnFlags.OnTitleLaunch,true);
 }
}
