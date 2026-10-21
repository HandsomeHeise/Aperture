using Aperture.Debugging;
using Aperture.Models;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class ModuleParityTests
{
 [Fact] public async Task ModuleFlagsComeFromNativePresenceTokens()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=> {Assert.Equal("modules",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"202- modules\r\nname=\"not-a-dll.xex\" base=0x1000 size=0x2000 xbe tls dll\r\nname=\"looks.dll\" base=0x3000 size=0x2000\r\n.\r\n",ct);});
  await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);
  var modules=await new DebugTarget(connection).GetModulesAsync();
  var flags=typeof(ModuleInfo).GetProperty("Flags");Assert.NotNull(flags);
  Assert.Equal(ModuleInfoFlags.Main|ModuleInfoFlags.Tls|ModuleInfoFlags.Dll,flags.GetValue(modules[0]));
  Assert.Equal((ModuleInfoFlags)0,flags.GetValue(modules[1]));
 }
}
