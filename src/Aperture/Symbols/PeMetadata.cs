using System.Reflection.PortableExecutable;

namespace Aperture.Symbols;

public sealed record PeImageInfo(uint TimeDateStamp, uint SizeOfImage, uint EntryPointRva, Guid? PdbGuid, int PdbAge, string? PdbPath);

public static class PeMetadata
{
    public static PeImageInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var header = reader.PEHeaders.PEHeader ?? throw new BadImageFormatException("Not a PE image.");
        Guid? guid = null; var age = 0; string? pdbPath = null;
        foreach (var entry in reader.ReadDebugDirectory())
        {
            if (entry.Type != DebugDirectoryEntryType.CodeView) continue;
            var codeView = reader.ReadCodeViewDebugDirectoryData(entry);
            guid = codeView.Guid; age = codeView.Age; pdbPath = codeView.Path; break;
        }
        return new(unchecked((uint)reader.PEHeaders.CoffHeader.TimeDateStamp), (uint)header.SizeOfImage, (uint)header.AddressOfEntryPoint, guid, age, pdbPath);
    }
}
