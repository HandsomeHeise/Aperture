using Aperture.Debugging;
using Aperture.Symbols;
using Xunit;

namespace Aperture.Tests;
public sealed class SymbolResolverTests
{
    [Fact]
    public void ResolvesPeByImageIdentityAndRejectsTimestampMismatch()
    {
        var path = typeof(PeMetadata).Assembly.Location;
        var pe = PeMetadata.Read(path);
        using var database = new ExecutableDatabase();
        database.AddExecutable(new ExecutableInfo(Guid.NewGuid(), "Aperture.xex", path, "", "", "Aperture.dll", pe.TimeDateStamp, pe.SizeOfImage, false));
        var resolver = new LocalSymbolResolver(database);
        var module = new ModuleInfo("Aperture.xex", 0x82000000, pe.SizeOfImage, 0, pe.TimeDateStamp, 0, 0, pe.SizeOfImage);
        Assert.Equal(path, resolver.Resolve(module)!.PortableExecutablePath);
        Assert.Null(resolver.Resolve(module with { Timestamp = pe.TimeDateStamp + 1 }));
        Assert.Equal(0x82000000u + pe.EntryPointRva, resolver.GetEntryPoint(module));
    }
}
