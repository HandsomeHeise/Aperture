using System.Buffers.Binary;
using System.Text;
using Aperture.Debugging;
using Aperture.Symbols;

namespace Aperture.Dumps;

public sealed class DumpReader : IDisposable
{
    private readonly FileStream stream;
    private readonly List<DumpMemoryRegion> regions = [];
    private readonly List<DumpMemoryRegion> stacks = [];
    private readonly Dictionary<uint, DumpThreadInfo> threads = [];
    private readonly List<ModuleInfo> modules = [];
    private readonly Dictionary<uint, DumpStreamInfo> streams = [];
    private LocalSymbolResolver? defaultSymbols;
    private ExecutableDatabase? imageDatabase;
    public ulong Flags { get; }
    public IReadOnlyList<DumpStreamInfo> Streams => streams.Values.ToArray();
    public bool IsDump => true;
    public IReadOnlyList<ModuleInfo> Modules => modules.AsReadOnly();
    public IReadOnlyList<uint> ThreadIds => threads.Keys.Order().ToArray();
    public IReadOnlyList<DumpMemoryRegion> MemoryRegions => regions.AsReadOnly();
    public DumpReader(string path)
    {
        stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var header = At(0, 32);
            if (!header.AsSpan(0, 4).SequenceEqual("MDMP"u8) || (U32(header, 4) & 0xffff) != 0xA793) throw new InvalidDataException("Unsupported dump signature or version.");
            Flags = U64(header, 24);
            var count = U32(header, 8); var directoryRva = U32(header, 12);
            if (count > 65536) throw new InvalidDataException("Too many dump streams.");
            var directory = At(directoryRva, checked((int)count * 12));
            var seen = new HashSet<uint>();
            for (var i = 0; i < count; i++)
            {
                var offset = i * 12; var type = U32(directory, offset); var size = U32(directory, offset + 4); var rva = U32(directory, offset + 8);
                Validate(rva, size);
                if (!seen.Add(type)) throw new InvalidDataException("Duplicate dump stream type.");
                streams.Add(type, new(type, size, rva));
                if (type is not (3 or 4 or 5 or 9)) continue;
                if (size > 64 * 1024 * 1024) throw new InvalidDataException("Dump metadata stream exceeds limits.");
                var data = At(rva, (int)size);
                switch (type)
                {
                    case 3: ReadThreads(data); break;
                    case 4: ReadModules(data); break;
                    case 5: ReadMemoryList(data); break;
                    case 9: ReadMemory64(data); break;
                }
            }
            // Thread stack descriptors may be the only memory in a small dump.
            // Add uncovered portions; explicit memory streams take precedence.
            foreach (var stack in stacks)
            {
                var cursor = (ulong)stack.Address;
                var end = cursor + stack.Length;
                foreach (var range in regions.OrderBy(r => r.Address).ToArray())
                {
                    var start = (ulong)range.Address; var stop = start + range.Length;
                    if (stop <= cursor || start >= end) continue;
                    if (start > cursor) AddRegion(cursor, start - cursor, stack.FileOffset + cursor - stack.Address);
                    cursor = Math.Max(cursor, stop);
                    if (cursor >= end) break;
                }
                if (cursor < end) AddRegion(cursor, end - cursor, stack.FileOffset + cursor - stack.Address);
            }
            regions.Sort((a, b) => a.Address.CompareTo(b.Address));
            for (var i = 1; i < regions.Count; i++) if ((ulong)regions[i-1].Address + regions[i-1].Length > regions[i].Address) throw new InvalidDataException("Overlapping dump memory regions.");
        }
        catch { stream.Dispose(); throw; }
    }
    public DumpReader(string path, string imageSearchPath) : this(path)
    {
        try
        {
            imageDatabase = ImageSearchPathResolver.CreateDatabase(imageSearchPath, modules);
            defaultSymbols = new(imageDatabase);
        }
        catch { Dispose(); throw; }
    }
    public static async Task<DumpReader> OpenAsync(string path, string imageSearchPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dump = new DumpReader(path);
        try
        {
            dump.imageDatabase = await ImageSearchPathResolver.CreateDatabaseAsync(imageSearchPath, dump.modules, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            dump.defaultSymbols = new(dump.imageDatabase);
            return dump;
        }
        catch { dump.Dispose(); throw; }
    }
    /// <summary>Reads an original stream without interpreting or discarding extension data.</summary>
    public byte[] ReadStream(uint type)
    {
        if (!streams.TryGetValue(type, out var info)) throw new KeyNotFoundException("Stream is absent from the dump.");
        if (info.Size > 64 * 1024 * 1024) throw new InvalidDataException("Stream exceeds the metadata read limit.");
        return At(info.FileOffset, (int)info.Size);
    }
    public byte[] ReadMemory(uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > 64 * 1024 * 1024 || (ulong)address + (uint)length > 0x1_0000_0000UL) throw new ArgumentOutOfRangeException(nameof(length));
        var result = new byte[length]; var position = (ulong)address; var copied = 0;
        while (copied < length)
        {
            var region = regions.FirstOrDefault(r => position >= r.Address && position < (ulong)r.Address + r.Length) ?? throw new EndOfStreamException("Requested memory is not present in the dump.");
            var offset = position - region.Address; var take = (int)Math.Min((ulong)(length - copied), region.Length - offset);
            At(region.FileOffset + offset, take).CopyTo(result, copied); copied += take; position += (uint)take;
        }
        return result;
    }
    public RegisterSet GetRegisters(uint threadId)
    {
        if (!threads.TryGetValue(threadId, out var thread)) throw new KeyNotFoundException("Thread is not in the dump.");
        if (thread.ContextLength == 0) throw new InvalidDataException("Thread context is absent from the dump.");
        return XenonContextCodec.Decode(At(thread.ContextRva, checked((int)thread.ContextLength)));
    }
    public DumpThreadInfo GetThreadInfo(uint threadId) => threads.TryGetValue(threadId, out var thread) ? thread : throw new KeyNotFoundException("Thread is not in the dump.");
    public ThreadInfo GetThreadMetadata(uint threadId)
    {
        var info = GetThreadInfo(threadId);
        var context = GetRegisters(threadId);
        var sp = context.Has64(Aperture.Models.Register64.r1) ? checked((uint)context.Get64(Aperture.Models.Register64.r1)) : info.StackAddress;
        return new(threadId, info.SuspendCount, info.Priority, checked((uint)info.Teb), 0, checked(info.StackAddress + info.StackLength), info.StackAddress, unchecked(sp - info.StackAddress), null, "", 0, 0);
    }
    public DebugEventInfo GetStopEventInfo(uint threadId)
    {
        _ = GetThreadInfo(threadId);
        if (!streams.TryGetValue(6, out var info) || info.Size == 0) return new(Aperture.Models.DebugEventType.NoEvent, false, threadId, null, null, null, "", 0, 0, 0, Array.Empty<uint>());
        var data = ReadStream(6);
        if (data.Length < 168) throw new InvalidDataException("Truncated exception stream.");
        if (U32(data, 0) != threadId) return new(Aperture.Models.DebugEventType.NoEvent, false, threadId, null, null, null, "", 0, 0, 0, Array.Empty<uint>());
        var count = U32(data, 32);
        if (count > 15 || U64(data, 24) > uint.MaxValue) throw new InvalidDataException("Invalid Xbox exception record.");
        var parameters = Enumerable.Range(0, (int)count).Select(i => checked((uint)U64(data, 40 + i * 8))).ToArray();
        var code = U32(data, 8);
        var type = code == 0x80000003 ? parameters.Length > 0 && parameters[0] == 27 ? Aperture.Models.DebugEventType.RIP : Aperture.Models.DebugEventType.ExecutionBreak : code == 0xC0000420 ? Aperture.Models.DebugEventType.AssertionFailedEx : Aperture.Models.DebugEventType.Exception;
        return new(type, true, threadId, null, null, null, "", type == Aperture.Models.DebugEventType.Exception ? code : 0, (uint)U64(data, 24), type == Aperture.Models.DebugEventType.Exception ? (Aperture.Models.ExceptionFlags)U32(data, 12) : 0, type == Aperture.Models.DebugEventType.Exception ? Array.AsReadOnly(parameters) : Array.Empty<uint>());
    }
    public StackFrame GetTopOfStack(uint threadId, LocalSymbolResolver? symbols = null)
    {
        symbols ??= defaultSymbols;
        var context = GetRegisters(threadId);
        var tables = new Dictionary<uint, FunctionTable>();
        Task<byte[]> Read(uint address, int length, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try { return Task.FromResult(ReadMemory(address, length)); }
            catch (EndOfStreamException)
            {
                var module = modules.FirstOrDefault(m => address >= m.BaseAddress && (ulong)address + (uint)length <= (ulong)m.BaseAddress + m.Size);
                if (module is null || symbols is null) throw;
                return Task.FromResult(symbols.ReadImageMemory(module, address, length));
            }
        }
        Task<FunctionInfo?> Lookup(uint address, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var module = modules.FirstOrDefault(m => address >= m.BaseAddress && (ulong)address < (ulong)m.BaseAddress + m.Size);
            if (module is null || symbols is null || symbols.Resolve(module) is null) return Task.FromResult<FunctionInfo?>(null);
            if (!tables.TryGetValue(module.BaseAddress, out var table)) tables[module.BaseAddress] = table = symbols.ReadFunctionTable(module);
            return Task.FromResult<FunctionInfo?>(table.Find(address) ?? new FunctionInfo(Aperture.Models.FunctionType.NoPData, module.BaseAddress, module.BaseAddress, checked(module.BaseAddress + module.Size)));
        }
        var function = Lookup(context.Get32(Aperture.Models.Register32.iar), CancellationToken.None).GetAwaiter().GetResult();
        return new(context, function, Read, Lookup, null);
    }
    private void ReadThreads(byte[] data)
    {
        var count = Count(data, 48);
        for (var i = 0; i < count; i++)
        {
            var o = 4 + i * 48; var id = U32(data, o); var stackAddress = U64(data, o + 24); var stackLength = U32(data, o + 32); var stackRva = U32(data, o + 36); var contextLength = U32(data, o + 40); var contextRva = U32(data, o + 44);
            Validate(stackRva, stackLength); Validate(contextRva, contextLength);
            if (contextLength > 65536 || stackAddress > uint.MaxValue) throw new InvalidDataException("Invalid Xbox thread record.");
            if (!threads.TryAdd(id, new(id, U32(data, o + 4), U32(data, o + 12), U64(data, o + 16), (uint)stackAddress, stackLength, contextRva, contextLength))) throw new InvalidDataException("Duplicate thread ID.");
            if (stackLength > 0)
            {
                if (stackAddress + stackLength > 0x1_0000_0000UL) throw new InvalidDataException("Stack exceeds Xbox address space.");
                stacks.Add(new((uint)stackAddress, stackLength, stackRva));
            }
        }
    }
    private void ReadModules(byte[] data)
    {
        var count = Count(data, 108);
        for (var i = 0; i < count; i++)
        {
            var o = 4 + i * 108; var address = U64(data, o);
            if (address > uint.MaxValue) throw new InvalidDataException("Module is outside Xbox address space.");
            var name = ReadString(U32(data, o + 20));
            modules.Add(new(name, (uint)address, U32(data, o + 8), U32(data, o + 12), U32(data, o + 16), 0, 0, U32(data, o + 8)) { FullName = name });
        }
    }
    private void ReadMemoryList(byte[] data)
    {
        var count = Count(data, 16);
        for (var i = 0; i < count; i++) { var o = 4 + i * 16; AddRegion(U64(data, o), U32(data, o + 8), U32(data, o + 12)); }
    }
    private void ReadMemory64(byte[] data)
    {
        if (data.Length < 16) throw new InvalidDataException("Truncated Memory64 stream.");
        var count = U64(data, 0); var rva = U64(data, 8);
        if (count > (ulong)(data.Length - 16) / 16) throw new InvalidDataException("Truncated Memory64 descriptors.");
        for (ulong i = 0; i < count; i++) { var o = checked(16 + (int)i * 16); var length = U64(data, o + 8); AddRegion(U64(data, o), length, rva); rva = checked(rva + length); }
    }
    private void AddRegion(ulong address, ulong length, ulong rva)
    {
        if (address > uint.MaxValue || length > 0x1_0000_0000UL - address) throw new InvalidDataException("Dump memory range exceeds Xbox address space.");
        Validate(rva, length); if (length > 0) regions.Add(new((uint)address, length, rva));
    }
    private string ReadString(uint rva)
    {
        var length = U32(At(rva, 4), 0);
        if (length > 1024 * 1024 || (length & 1) != 0) throw new InvalidDataException("Invalid dump string length.");
        return Encoding.Unicode.GetString(At((ulong)rva + 4, (int)length));
    }
    private byte[] At(ulong offset, int length)
    {
        Validate(offset, (ulong)length);
        var bytes = new byte[length];
        lock (stream) { stream.Position = (long)offset; stream.ReadExactly(bytes); }
        return bytes;
    }
    private void Validate(ulong offset, ulong length)
    {
        if (offset > (ulong)stream.Length || length > (ulong)stream.Length - offset) throw new InvalidDataException("Dump record points outside the file.");
    }
    private static int Count(byte[] data, int recordSize)
    {
        if (data.Length < 4 || U32(data, 0) > (data.Length - 4) / recordSize) throw new InvalidDataException("Truncated dump record list.");
        return checked((int)U32(data, 0));
    }
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
    public void Dispose() { stream.Dispose(); imageDatabase?.Dispose(); }
}

public sealed record DumpMemoryRegion(uint Address, ulong Length, ulong FileOffset);
public sealed record DumpThreadInfo(uint ThreadId, uint SuspendCount, uint Priority, ulong Teb, uint StackAddress, uint StackLength, uint ContextRva, uint ContextLength);

public sealed record DumpStreamInfo(uint Type, uint Size, uint FileOffset);

