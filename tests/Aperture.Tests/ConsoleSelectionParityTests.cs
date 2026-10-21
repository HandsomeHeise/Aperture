using Aperture.Consoles;
using Aperture.Models;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class ConsoleSelectionParityTests
{
 [Theory]
 [InlineData(SelectConsoleFlags.NoPromptIfDefaultExists,false,"first")]
 [InlineData(SelectConsoleFlags.NoPromptIfOnlyOne,true,"second")]
 public async Task SelectionFlagsControlDefaultVersusExplicitSelection(SelectConsoleFlags flags,bool expectedPrompt,string expected)
 {
  var settings=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
  try {var manager=new ConsoleManager(settings);manager.SetDefaultConsole("first");manager.AddConsole("second");var prompted=false;Func<IReadOnlyList<string>,CancellationToken,Task<string>> select=(choices,ct)=>{prompted=true;Assert.Equal(new[]{"first","second"},choices);return Task.FromResult("second");};
  var method=typeof(ConsoleManager).GetMethod("SelectConsoleAsync");Assert.NotNull(method);
  var result=await (Task<string>)method.Invoke(manager,[(AccessFlags)0,flags,select,null,730,CancellationToken.None])!;Assert.Equal(expected,result);Assert.Equal(expectedPrompt,prompted);
  }finally{if(File.Exists(settings))File.Delete(settings);}
 }
 [Fact] public async Task AccessFilterQueriesCurrentUserBeforeReturningDefault()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- kit\r\n",ct);Assert.Equal("getuserpriv me",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- read write\r\n",ct);});
  var settings=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{var manager=new ConsoleManager(settings);manager.SetDefaultConsole("127.0.0.1");var method=typeof(ConsoleManager).GetMethod("SelectConsoleAsync");Assert.NotNull(method);var selected=await (Task<string>)method.Invoke(manager,[AccessFlags.Read|AccessFlags.Write,SelectConsoleFlags.FilterByAccess,null,null,server.Port,CancellationToken.None])!;Assert.Equal("127.0.0.1",selected);}finally{if(File.Exists(settings))File.Delete(settings);}
 }
}
