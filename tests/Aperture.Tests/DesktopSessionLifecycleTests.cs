using Aperture.Consoles;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class DesktopSessionLifecycleTests
{
 [Fact] public async Task ClosingBrowserSessionCancelsActiveRemoteReadWithoutThrowing()
 {
  var requested=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("dirlist name=\"Hdd:\\\"",await DebugMonitorServer.ReadLine(s,ct));requested.SetResult();await Task.Delay(Timeout.Infinite,ct);});
  var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));await using var session=await manager.OpenWindowsExplorerAsync("127.0.0.1",@"Hdd:\",_=>{},port:server.Port);using var http=new HttpClient();var pending=http.GetAsync(session.Url);await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));await session.DisposeAsync();try {using var response=await pending;}catch(HttpRequestException){}
 }
}
