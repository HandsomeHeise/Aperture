using Aperture.Debugging;
using Aperture.Symbols;
using Xunit;
namespace Aperture.Tests;
public sealed class SymbolPointerParityTests
{
 [Fact] public async Task StandaloneCacheDirectiveResolvesStoredImage()
 {
  var image=typeof(PeMetadata).Assembly.Location; var info=PeMetadata.Read(image); var name=Path.GetFileName(image); var module=new ModuleInfo(name,0,info.SizeOfImage,0,info.TimeDateStamp,0,0,0);
  var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N")); var target=Path.Combine(cache,name,$"{info.TimeDateStamp:X8}{info.SizeOfImage:X}",name);
  Directory.CreateDirectory(Path.GetDirectoryName(target)!);
  try { File.Copy(image,target); using var database=await ImageSearchPathResolver.CreateDatabaseAsync("cache*"+cache,[module]); Assert.NotNull(new LocalSymbolResolver(database).Resolve(module)); }
  finally { Directory.Delete(cache,true); }
 }
 [Fact] public async Task FilePointerReadsIdentityMatchedImageAndPopulatesDownstreamStore()
 {
  var image=typeof(PeMetadata).Assembly.Location;var metadata=PeMetadata.Read(image);var name=Path.GetFileName(image);var module=new ModuleInfo(name,0x82000000,metadata.SizeOfImage,0,metadata.TimeDateStamp,0,0,0);var cache=Path.Combine(Path.GetTempPath(),"aperture-symbol-"+Guid.NewGuid().ToString("N"));var directory=Path.Combine(cache,name,$"{metadata.TimeDateStamp:X8}{metadata.SizeOfImage:X}");Directory.CreateDirectory(directory);try{await File.WriteAllTextAsync(Path.Combine(directory,"file.ptr"),image);var resolved=await SymbolServerImageResolver.ResolveAsync(module,[cache]);Assert.Equal(Path.Combine(directory,name),resolved);Assert.Equal(await File.ReadAllBytesAsync(image),await File.ReadAllBytesAsync(resolved!));}finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(cache));Directory.Delete(cache,true);}
 }
}
