using Aperture.Debugging;
using Aperture.Symbols;
using System.Net;
using System.Net.Sockets;
using Xunit;
namespace Aperture.Tests;
public sealed class SymbolServerParityTests
{
 [Fact] public async Task ServerImageDownloadsByTimestampAndSizeThenResolvesFromCacheOffline()
 {
  var image=typeof(PeMetadata).Assembly.Location;var metadata=PeMetadata.Read(image);var name=Path.GetFileName(image);var module=new ModuleInfo(name,0x82000000,metadata.SizeOfImage,0,metadata.TimeDateStamp,0,0,0);var key=$"{metadata.TimeDateStamp:X8}{metadata.SizeOfImage:X}";
  using var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();using var server=new HttpListener();server.Prefixes.Add($"http://127.0.0.1:{port}/");server.Start();
  var worker=Task.Run(async()=>{var context=await server.GetContextAsync();Assert.Equal($"/{name}/{key}/{name}",context.Request.Url!.AbsolutePath);var data=await File.ReadAllBytesAsync(image);context.Response.ContentLength64=data.Length;await context.Response.OutputStream.WriteAsync(data);context.Response.Close();});
  var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N"));try{
   var method=typeof(ImageSearchPathResolver).GetMethod("CreateDatabaseAsync");Assert.NotNull(method);var path=$"cache*{cache};srv*http://127.0.0.1:{port}/";
   using var database=await (Task<ExecutableDatabase>)method.Invoke(null,[path,new[]{module},CancellationToken.None])!;var resolved=new LocalSymbolResolver(database).Resolve(module);Assert.NotNull(resolved);Assert.Equal(Path.Combine(cache,name,key,name),resolved.PortableExecutablePath);await worker;server.Stop();
   using var cached=await (Task<ExecutableDatabase>)method.Invoke(null,[path,new[]{module},CancellationToken.None])!;Assert.NotNull(new LocalSymbolResolver(cached).Resolve(module));
  }finally{if(Directory.Exists(cache)){Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(cache));Directory.Delete(cache,true);}}
 }
}
