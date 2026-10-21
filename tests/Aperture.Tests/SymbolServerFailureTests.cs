using Aperture.Debugging;
using Aperture.Symbols;
using System.Net;
using System.Net.Sockets;
using Xunit;
namespace Aperture.Tests;
public sealed class SymbolServerFailureTests
{
 [Fact] public async Task WrongImageIdentityNeverPublishesCacheFile()
 {
  var image=typeof(PeMetadata).Assembly.Location; var info=PeMetadata.Read(image); var name=Path.GetFileName(image); var module=new ModuleInfo(name,0,info.SizeOfImage,0,info.TimeDateStamp+1,0,0,0);
  var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N"));var directory=Path.Combine(cache,name,$"{module.Timestamp:X8}{module.Size:X}");Directory.CreateDirectory(directory);
  try { await File.WriteAllTextAsync(Path.Combine(directory,"file.ptr"),image); await Assert.ThrowsAsync<InvalidDataException>(()=>SymbolServerImageResolver.ResolveAsync(module,[cache])); Assert.False(File.Exists(Path.Combine(directory,name))); Assert.Empty(Directory.GetFiles(directory,"*.tmp")); }
  finally { Directory.Delete(cache,true); }
 }
 [Fact] public async Task CancellationStopsResolutionBeforeStoreAccess()
 {
  var module=new ModuleInfo("image.exe",0,1,0,1,0,0,0); using var ct=new CancellationTokenSource(); ct.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>SymbolServerImageResolver.ResolveAsync(module,["http://127.0.0.1:1"],ct.Token));
 }
 [Fact] public async Task OversizedHttpResponseRejectedBeforeCaching()
 {
  using var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();using var server=new HttpListener();server.Prefixes.Add($"http://127.0.0.1:{port}/");server.Start();
  var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var worker=Task.Run(async()=>{var context=await server.GetContextAsync();context.Response.ContentLength64=64L*1024*1024+1;await context.Response.OutputStream.WriteAsync(new byte[]{0});await context.Response.OutputStream.FlushAsync();await done.Task;context.Response.Abort();});
  var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N"));
  try { await Assert.ThrowsAsync<InvalidDataException>(()=>SymbolServerImageResolver.ResolveAsync(new ModuleInfo("image.exe",0,1,0,1,0,0,0),[cache,$"http://127.0.0.1:{port}"]));Assert.False(Directory.Exists(cache)); }
  finally { done.TrySetResult();await worker;server.Stop(); }
 }
 [Fact] public async Task StalledBodyStopsAtWholeResolutionDeadline()
 {
  using var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();using var server=new HttpListener();server.Prefixes.Add($"http://127.0.0.1:{port}/");server.Start();
  var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var worker=Task.Run(async()=>{var context=await server.GetContextAsync();context.Response.ContentLength64=1024;await context.Response.OutputStream.WriteAsync(new byte[]{0});await context.Response.OutputStream.FlushAsync();started.SetResult();await done.Task;context.Response.Abort();});
  var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N"));
  try { var resolution=SymbolServerImageResolver.ResolveAsync(new ModuleInfo("image.exe",0,1,0,1,0,0,0),[cache,$"http://127.0.0.1:{port}"],TimeSpan.FromMilliseconds(200));await started.Task.WaitAsync(TimeSpan.FromSeconds(3));await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await resolution.WaitAsync(TimeSpan.FromSeconds(3)));Assert.False(Directory.Exists(cache)); }
  finally { done.TrySetResult();await worker;server.Stop(); }
 }
}
