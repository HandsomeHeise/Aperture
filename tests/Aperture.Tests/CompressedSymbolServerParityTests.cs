using System.Buffers.Binary;
using Aperture.Debugging;
using Aperture.Symbols;
using System.Net;
using System.Net.Sockets;
using Xunit;
namespace Aperture.Tests;
public sealed class CompressedSymbolServerParityTests
{
 [Fact] public async Task CompressedUnderscoreImageExpandsToValidatedCacheEntry()
 {
  if(!OperatingSystem.IsWindows()) return;
  var image=typeof(PeMetadata).Assembly.Location;var metadata=PeMetadata.Read(image);var name=Path.GetFileName(image);var module=new ModuleInfo(name,0x82000000,metadata.SizeOfImage,0,metadata.TimeDateStamp,0,0,0);
  using var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();using var server=new HttpListener();server.Prefixes.Add($"http://127.0.0.1:{port}/");server.Start();
  var worker=Task.Run(async()=>{var first=await server.GetContextAsync();first.Response.StatusCode=404;first.Response.Close();var second=await server.GetContextAsync();Assert.EndsWith(name[..^1]+"_",second.Request.Url!.AbsolutePath);var cab=Cab(name,await File.ReadAllBytesAsync(image));second.Response.ContentLength64=cab.Length;await second.Response.OutputStream.WriteAsync(cab);second.Response.Close();});
  var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N"));try{var resolved=await SymbolServerImageResolver.ResolveAsync(module,[cache,$"http://127.0.0.1:{port}/"]);Assert.NotNull(resolved);Assert.Equal(await File.ReadAllBytesAsync(image),await File.ReadAllBytesAsync(resolved));await worker;}finally{if(Directory.Exists(cache)){Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(cache));Directory.Delete(cache,true);}}
 }
 private static byte[] Cab(string name,byte[] data)
 {
  var filename=System.Text.Encoding.Latin1.GetBytes(name+'\0');var start=60+filename.Length;var blocks=(data.Length+32767)/32768;var cab=new byte[start+data.Length+8*blocks];"MSCF"u8.CopyTo(cab);BinaryPrimitives.WriteUInt32LittleEndian(cab.AsSpan(8),(uint)cab.Length);BinaryPrimitives.WriteUInt32LittleEndian(cab.AsSpan(16),44);cab[24]=3;cab[25]=1;BinaryPrimitives.WriteUInt16LittleEndian(cab.AsSpan(26),1);BinaryPrimitives.WriteUInt16LittleEndian(cab.AsSpan(28),1);BinaryPrimitives.WriteUInt32LittleEndian(cab.AsSpan(36),(uint)start);BinaryPrimitives.WriteUInt16LittleEndian(cab.AsSpan(40),(ushort)blocks);BinaryPrimitives.WriteUInt32LittleEndian(cab.AsSpan(44),(uint)data.Length);filename.CopyTo(cab,60);var source=0;var target=start;while(source<data.Length){var count=Math.Min(32768,data.Length-source);BinaryPrimitives.WriteUInt16LittleEndian(cab.AsSpan(target+4),(ushort)count);BinaryPrimitives.WriteUInt16LittleEndian(cab.AsSpan(target+6),(ushort)count);data.AsSpan(source,count).CopyTo(cab.AsSpan(target+8));source+=count;target+=8+count;}return cab;
 }
}


