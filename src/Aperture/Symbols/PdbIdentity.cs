using System.Buffers.Binary;
using System.Reflection.Metadata;

namespace Aperture.Symbols;

public sealed record PdbIdentity(Guid Guid, uint Age)
{
    public static PdbIdentity Read(string path)
    {
        using var stream = File.OpenRead(path);
        var signature = new byte[32]; stream.ReadExactly(signature);
        if (signature.AsSpan(0, 4).SequenceEqual("BSJB"u8))
        {
            stream.Position = 0;
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var header = provider.GetMetadataReader().DebugMetadataHeader ?? throw new InvalidDataException("Portable PDB has no identity.");
            return new(new Guid(header.Id.AsSpan(0, 16)), 1);
        }
        if (!signature.AsSpan().SequenceEqual("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8)) throw new InvalidDataException("Unsupported PDB format.");
        var super = new byte[24]; stream.ReadExactly(super);
        var pageSize = BinaryPrimitives.ReadUInt32LittleEndian(super);
        var pageCount = BinaryPrimitives.ReadUInt32LittleEndian(super.AsSpan(8));
        var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(super.AsSpan(12));
        var pageMap = BinaryPrimitives.ReadUInt32LittleEndian(super.AsSpan(20));
        if (pageSize is < 512 or > 65536 || (pageSize & (pageSize - 1)) != 0 || directorySize > 64 * 1024 * 1024 || (ulong)pageCount * pageSize > (ulong)stream.Length) throw new InvalidDataException("Invalid MSF superblock.");
        var pages = checked((int)((directorySize + pageSize - 1) / pageSize));
        if ((uint)pages * 4 > pageSize) throw new InvalidDataException("MSF directory map exceeds one page.");
        var map = ReadAt((ulong)pageMap * pageSize, checked(pages * 4));
        var directory = new byte[directorySize];
        for (var i = 0; i < pages; i++)
        {
            var index = BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(i * 4));
            var count = Math.Min((int)pageSize, directory.Length - i * (int)pageSize);
            ReadAt((ulong)index * pageSize, count).CopyTo(directory, i * (int)pageSize);
        }
        if (directory.Length < 12) throw new InvalidDataException("PDB stream directory is missing.");
        var streams = BinaryPrimitives.ReadUInt32LittleEndian(directory);
        if (streams < 2 || (ulong)streams * 4 + 4 > (ulong)directory.Length) throw new InvalidDataException("Invalid PDB stream count.");
        var firstSize = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(4));
        var infoSize = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(8));
        if (infoSize < 28 || infoSize > 64 * 1024 * 1024) throw new InvalidDataException("Invalid PDB information stream.");
        var listOffset = checked(4 + (int)streams * 4 + (firstSize == uint.MaxValue ? 0 : (int)((firstSize + (ulong)pageSize - 1) / pageSize) * 4));
        if (listOffset + 4 > directory.Length) throw new InvalidDataException("Missing PDB information stream pages.");
        var firstPage = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(listOffset));
        var info = ReadAt((ulong)firstPage * pageSize, 28);
        return new(new Guid(info.AsSpan(12, 16)), BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(8)));

        byte[] ReadAt(ulong offset, int count)
        {
            if (offset > (ulong)stream.Length || (ulong)count > (ulong)stream.Length - offset) throw new InvalidDataException("PDB page is outside the file.");
            stream.Position = (long)offset; var bytes = new byte[count]; stream.ReadExactly(bytes); return bytes;
        }
    }
}
