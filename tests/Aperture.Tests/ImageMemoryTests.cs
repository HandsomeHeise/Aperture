using System.Reflection.PortableExecutable;
using Aperture.Debugging;
using Aperture.Symbols;
using Xunit;
namespace Aperture.Tests;

public sealed class ImageMemoryTests
{
    [Fact]
    public void ResolvesCodeBytesAtRelocatedModuleAddress()
    {
        var path = typeof(PeMetadata).Assembly.Location;
        var metadata = PeMetadata.Read(path);
        var module = new ModuleInfo(Path.GetFileName(path), 0x82000000, metadata.SizeOfImage, 0, metadata.TimeDateStamp, 0, 0, 0);
        using var database = ImageSearchPathResolver.CreateDatabase(path, [module]);
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var section = pe.PEHeaders.SectionHeaders.First(s => s.Name == ".text");
        var resolver = new LocalSymbolResolver(database);
        Assert.Equal(pe.GetSectionData(section.VirtualAddress).GetContent(0, 16).ToArray(), resolver.ReadImageMemory(module, module.BaseAddress + (uint)section.VirtualAddress, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.ReadImageMemory(module, module.BaseAddress - 4, 16));
    }
}
