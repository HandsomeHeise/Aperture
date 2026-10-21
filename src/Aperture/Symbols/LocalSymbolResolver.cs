using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Aperture.Debugging;

namespace Aperture.Symbols;

/// <summary>Resolves module image metadata from the independently stored executable database.</summary>
public sealed class LocalSymbolResolver(ExecutableDatabase database)
{
    /// <summary>Reads file-backed image bytes at their relocated Xbox address for offline unwinding.</summary>
    public byte[] ReadImageMemory(ModuleInfo module, uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > 64 * 1024 * 1024 || address < module.BaseAddress || (ulong)address + (uint)length > (ulong)module.BaseAddress + module.Size) throw new ArgumentOutOfRangeException(nameof(address));
        var executable = Resolve(module) ?? throw new FileNotFoundException("Module has no matching executable image.");
        using var stream = File.OpenRead(executable.PortableExecutablePath); using var pe = new PEReader(stream);
        var rva = checked((int)(address - module.BaseAddress));
        var content = pe.GetSectionData(rva).GetContent();
        if (content.Length < length) throw new EndOfStreamException("Requested code is not backed by the executable image.");
        return content.AsSpan(0, length).ToArray();
    }
    public ExecutableInfo? Resolve(ModuleInfo module)
    {
        var name = Path.GetFileNameWithoutExtension(module.Name);
        var matches = database.Executables.Where(e => e.TimeDateStamp == module.Timestamp && e.SizeOfImage == module.Size && string.Equals(Path.GetFileNameWithoutExtension(e.ModuleName), name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch { 0 => null, 1 => matches[0], _ => throw new InvalidDataException("Multiple executable identities match the module.") };
    }
    public uint GetEntryPoint(ModuleInfo module)
    {
        var executable = Resolve(module) ?? throw new FileNotFoundException("Module has no matching executable in the database.");
        return checked(module.BaseAddress + PeMetadata.Read(executable.PortableExecutablePath).EntryPointRva);
    }
    public FunctionTable ReadFunctionTable(ModuleInfo module)
    {
        var executable = Resolve(module) ?? throw new FileNotFoundException("Module has no matching executable in the database.");
        using var stream = File.OpenRead(executable.PortableExecutablePath); using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader ?? throw new BadImageFormatException("Executable is not a PE image.");
        var directory = header.ExceptionTableDirectory;
        if (directory.Size == 0) return new(ReadOnlySpan<byte>.Empty);
        if ((ushort)pe.PEHeaders.CoffHeader.Machine is not (0x1f0 or 0x1f2)) throw new NotSupportedException("Runtime function tables require a PowerPC image.");
        if (directory.Size < 0 || directory.Size > 64 * 1024 * 1024 || (directory.Size & 7) != 0) throw new InvalidDataException("Invalid Xenon PE runtime function table.");
        var bytes = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size).ToArray();
        var delta = (long)module.BaseAddress - checked((long)header.ImageBase);
        for (var offset = 0; offset < bytes.Length; offset += 8)
        {
            var address = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), checked((uint)(address + delta)));
        }
        return new(bytes);
    }
}
