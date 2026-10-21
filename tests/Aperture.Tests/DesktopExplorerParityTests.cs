using Aperture.Consoles;
using Aperture.Tests.Fixtures;
using System.Net;
using Xunit;
namespace Aperture.Tests;
public sealed class DesktopExplorerParityTests
{
 [Fact] public async Task ExplorerRendersRemoteDirectoryAndRejectsTraversalBeforeSendingCommands()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("dirlist name=\"Hdd:\\\"",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"202- listing\r\nname=\"notes&more.txt\" sizelo=4\r\n.\r\n",ct);await Task.Delay(1000,ct);});
  var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
  var method=typeof(ConsoleManager).GetMethod("OpenWindowsExplorerAsync");Assert.NotNull(method);
  var session=await (Task<ConsoleDesktopSession>)method.Invoke(manager,["127.0.0.1",@"Hdd:\",new Action<Uri>(_=>{}),new ConsoleConnectOptions(),server.Port,CancellationToken.None])!;
  await using var dispose=session;using var http=new HttpClient();var page=await http.GetStringAsync(session.Url);Assert.Contains("notes&amp;more.txt",page);Assert.Contains("Download",page);
  using var traversal=await http.GetAsync(new Uri(session.Url,"?path="+Uri.EscapeDataString(@"Hdd:\..\secret")));Assert.Equal(HttpStatusCode.BadRequest,traversal.StatusCode);
 }
}
