using Aperture.Consoles;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class SharedMemoryParityTests
{
 [Fact] public async Task SharedLeaseWriteInvalidatesOtherLeasesReadCache()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{foreach(var pair in new[]{("dbgname","200- kit\r\n"),("getmem addr=0x00001000 length=0x1","202- memory\r\n01\r\n.\r\n"),("setmem addr=0x00001000 data=02","200- set=1\r\n"),("getmem addr=0x00001000 length=0x1","202- memory\r\n02\r\n.\r\n")}){Assert.Equal(pair.Item1,await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,pair.Item2,ct);}});
  var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));var options=new ConsoleConnectOptions{Shared=true};
  await using var first=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",options,server.Port);await using var second=await manager.OpenConsoleWithOptionsAsync("127.0.0.1",options,server.Port);
  first.Memory.CacheEnabled=true;Assert.Equal(new byte[]{1},await first.Memory.ReadAsync(0x1000,1));await second.Memory.WriteAsync(0x1000,(ReadOnlyMemory<byte>)new byte[]{2});Assert.Equal(new byte[]{2},await first.Memory.ReadAsync(0x1000,1));
 }
}

