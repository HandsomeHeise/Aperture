using Aperture.Debugging;
using Aperture.Models;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class ModuleDetailParityTests
{
 [Fact] public async Task ModuleInfoProjectionIncludesVerifiedFullNameAndFlags()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("xbeinfo name=\"default.xex\"",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"202- info\r\nname=\"Hdd:\\game\\default.xex\"\r\n.\r\n",ct);});
  await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);
  var module=new DebugTarget(connection).GetModule(new ModuleInfo("default.xex",0x1000,0x100,0,0,0,0,0){Flags=ModuleInfoFlags.Main});
  var method=typeof(ConsoleModule).GetMethod("GetInfoAsync");Assert.NotNull(method);
  var info=await (Task<ModuleInfo>)method.Invoke(module,[CancellationToken.None])!;
  Assert.Equal(@"Hdd:\game\default.xex",typeof(ModuleInfo).GetProperty("FullName")!.GetValue(info));Assert.Equal(ModuleInfoFlags.Main,info.Flags);
 }
 [Fact] public async Task VerifiedModuleWithoutPDataProducesExplicitNoPDataMetadata()
 {
  await using var server=new DebugMonitorServer((s,ct)=>Task.Delay(1000,ct));await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);
  var target=new DebugTarget(connection);var module=new ModuleInfo("leaf.xex",0x1000,0x100,0,0,0,0,0);
  var function=await target.GetFunctionInfoAsync(module,0x1020);Assert.NotNull(function);Assert.Equal(FunctionType.NoPData,function.FunctionType);Assert.Equal(0x1000u,function.BeginAddress);Assert.Equal(0x1100u,function.FunctionEndAddress);
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>target.GetFunctionInfoAsync(module,0x1100));
 }
}
