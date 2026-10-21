using Aperture.Consoles;
using Aperture.Dumps;
using Xunit;
namespace Aperture.Tests;
public sealed class ManagerDumpParityTests
{
 [Fact] public async Task ManagerOpensDumpWithAsyncImageSearch()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dmp");
  try { DumpWriter.Write(path,[],[],[]);var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));var method=typeof(ConsoleManager).GetMethod("OpenDumpFileAsync",[typeof(string),typeof(string),typeof(CancellationToken)]);Assert.NotNull(method);using var dump=await (Task<DumpReader>)method.Invoke(manager,[path,Path.GetTempPath(),CancellationToken.None])!;Assert.True(dump.IsDump); }
  finally { File.Delete(path); }
 }
 [Fact] public void ManagerOpensDumpWithLocalImageSearchPath()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dmp");try{DumpWriter.Write(path,[],[],[]);var manager=new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));var method=typeof(ConsoleManager).GetMethod("OpenDumpFile",[typeof(string),typeof(string)]);Assert.NotNull(method);using var dump=(DumpReader)method.Invoke(manager,[path,Path.GetTempPath()])!;Assert.True(dump.IsDump);}finally{if(File.Exists(path))File.Delete(path);}
 }
}
