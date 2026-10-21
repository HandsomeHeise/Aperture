using Aperture.Debugging;
using Aperture.Symbols;
using Xunit;
namespace Aperture.Tests;
public sealed class ImageSearchPathParityTests
{
 [Fact] public void LocalImageSearchMatchesModuleIdentityRatherThanFileNameAlone()
 {
  var path=typeof(PeMetadata).Assembly.Location;var metadata=PeMetadata.Read(path);var module=new ModuleInfo(Path.GetFileName(path),0x82000000,metadata.SizeOfImage,0,metadata.TimeDateStamp,0,0,0);
  var factory=typeof(PeMetadata).Assembly.GetType("Aperture.Symbols.ImageSearchPathResolver");Assert.NotNull(factory);var method=factory.GetMethod("CreateDatabase");Assert.NotNull(method);
  using var database=(ExecutableDatabase)method.Invoke(null,[path,new[]{module}])!;var resolved=new LocalSymbolResolver(database).Resolve(module);Assert.NotNull(resolved);Assert.Equal(path,resolved.PortableExecutablePath);
 }
}
