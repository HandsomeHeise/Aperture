using Aperture.Consoles;
using Aperture.Tests.Fixtures;
using System.Net;
using Xunit;
namespace Aperture.Tests;
public sealed class DesktopUploadParityTests
{
 [Fact] public async Task ExplorerUploadCreatesNewFileAndTransfersExactBytes()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("fileeof name=\"Hdd:\\new.bin\" size=0 mustcreate",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- OK\r\n",ct);Assert.Equal("writefile name=\"Hdd:\\new.bin\" offset=0 length=3",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"204- send\r\n",ct);var bytes=new byte[3];await s.ReadExactlyAsync(bytes,ct);Assert.Equal(new byte[]{1,2,3},bytes);await DebugMonitorServer.Write(s,"200- 3\r\n",ct);});
  var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
  await using var session=await manager.OpenWindowsExplorerAsync("127.0.0.1",@"Hdd:\",_=>{},port:server.Port);
  using var http=new HttpClient();using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(session.Url,"upload?path="+Uri.EscapeDataString(@"Hdd:\")+"&name=new.bin")){Content=new ByteArrayContent([1,2,3])};request.Headers.Add("Origin",session.Url.GetLeftPart(UriPartial.Authority));
  using var response=await http.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("File uploaded",await response.Content.ReadAsStringAsync());
 }
}
