using System.Text;
using Aperture.Debugging;
using Aperture.Models;

namespace Aperture.Dumps;

/// <summary>Writes a standard minidump with Xenon contexts, modules and selected memory.</summary>
public static class DumpWriter
{
    public static void Write(string path, ModuleInfo[] modules, DumpThreadSnapshot[] threads, DumpMemorySnapshot[] memory)
        => WriteWithFlags(path, modules, threads, memory, 0);
    public static void WriteWithFlags(string path, ModuleInfo[] modules, DumpThreadSnapshot[] threads, DumpMemorySnapshot[] memory, DumpFlags flags, DebugEventInfo? exception = null)
    {
        flags = NormalizeFlags(flags);
        var fullMemory = flags.HasFlag(DumpFlags.WithFullMemory);
        ArgumentNullException.ThrowIfNull(modules); ArgumentNullException.ThrowIfNull(threads); ArgumentNullException.ThrowIfNull(memory);
        // Snapshot caller-owned arrays before validating and serializing.
        threads = threads.Select(t => t with { Context = (byte[])t.Context.Clone() }).ToArray();
        memory = memory.Select(m => m with { Bytes = (byte[])m.Bytes.Clone() }).OrderBy(m => m.Address).ToArray();
        if (threads.Select(t => t.ThreadId).Distinct().Count() != threads.Length) throw new ArgumentException("Duplicate thread IDs.", nameof(threads));
        foreach (var thread in threads) if (thread.Context.Length > 0) _ = XenonContextCodec.Decode(thread.Context);
        // Native dump loader 0x430800 requires the ABI frame size (including trailing alignment).
        threads = threads.Select(t =>
        {
            if (t.Context.Length == 0 || t.Context.Length >= 2624) return t;
            var frame = new byte[2624]; t.Context.CopyTo(frame, 0); return t with { Context = frame };
        }).ToArray();
        ulong end = 0;
        foreach (var range in memory)
        {
            if (range.Address < end || (ulong)range.Address + (uint)range.Bytes.Length > 0x1_0000_0000UL) throw new ArgumentException("Overlapping or out-of-range dump memory.", nameof(memory));
            end = (ulong)range.Address + (uint)range.Bytes.Length;
        }
        if (flags.HasFlag(DumpFlags.FilterMemory))
            foreach (var range in memory.Where(m => m.Filterable))
                for (var offset = checked((int)((4u - range.Address % 4u) % 4u)); offset + 4 <= range.Bytes.Length; offset += 4)
                {
                    var location = (ulong)range.Address + (uint)offset;
                    if (threads.Length > 0 && !threads.Any(t => location >= t.StackAddress && location < (ulong)t.StackAddress + t.StackLength)) continue;
                    var pointer = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(range.Bytes.AsSpan(offset, 4));
                    if (modules.Any(m => pointer >= m.BaseAddress && (ulong)pointer < (ulong)m.BaseAddress + m.Size)
                        || threads.Any(t => pointer >= t.StackAddress && (ulong)pointer < (ulong)t.StackAddress + t.StackLength)) continue;
                    range.Bytes.AsSpan(offset, 4).Clear();
                }
        if (exception is not null && (exception.Parameters.Count > 15 || !threads.Any(t => t.ThreadId == exception.ThreadId))) throw new ArgumentException("Exception must reference a captured thread and contain at most fifteen parameters.", nameof(exception));
        var moduleRva = exception is null ? 92u : 104u;
        var threadRva = checked(moduleRva + 4u + (uint)modules.Length * 108u);
        var memoryRva = checked(threadRva + 4u + (uint)threads.Length * 48u);
        var systemRva = checked(memoryRva + (fullMemory ? 16u : 4u) + (uint)memory.Length * 16u);
        var miscRva = systemRva + 56u;
        var exceptionRva = miscRva + 24u;
        var payloadRva = exceptionRva + (exception is null ? 0u : 168u);
        var names = modules.Select(m =>
        {
            var name = m.FullName.Length > 0 ? m.FullName : m.Name;
            return Encoding.Unicode.GetBytes(flags.HasFlag(DumpFlags.FilterModulePaths) ? name[(name.LastIndexOfAny(['\\', '/']) + 1)..] : name);
        }).ToArray();
        var nameOffsets = new uint[names.Length]; var contextOffsets = new uint[threads.Length]; var memoryOffsets = new uint[memory.Length];
        var version = Encoding.Unicode.GetBytes("Xenon");
        var cursor = payloadRva + 6u + (uint)version.Length;
        for (var i = 0; i < names.Length; i++) { nameOffsets[i] = cursor; cursor = checked(cursor + 4u + (uint)names[i].Length); }
        for (var i = 0; i < threads.Length; i++) { contextOffsets[i] = cursor; cursor = checked(cursor + (uint)threads[i].Context.Length); }
        for (var i = 0; i < memory.Length; i++) { memoryOffsets[i] = cursor; cursor = checked(cursor + (uint)memory[i].Bytes.Length); }
        // CreateNew prevents replacing an existing dump. Delete only our partial file on failure.
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            writer.Write("MDMP"u8); writer.Write(0xA793u); writer.Write(exception is null ? 5u : 6u); writer.Write(32u); writer.Write(0u); writer.Write(checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds())); writer.Write((ulong)flags);
            Directory(4, threadRva - moduleRva, moduleRva); Directory(3, memoryRva - threadRva, threadRva); Directory(fullMemory ? 9u : 5u, systemRva - memoryRva, memoryRva); Directory(7,56,systemRva); Directory(15,24,miscRva);
            if (exception is not null) Directory(6, 168, exceptionRva);
            writer.Write((uint)modules.Length);
            for (var i = 0; i < modules.Length; i++)
            {
                var module = modules[i]; writer.Write((ulong)module.BaseAddress); writer.Write(module.Size); writer.Write(module.Checksum); writer.Write(module.Timestamp); writer.Write(nameOffsets[i]); writer.Write(new byte[84]);
            }
            writer.Write((uint)threads.Length);
            for (var i = 0; i < threads.Length; i++)
            {
                var thread = threads[i];
                var stackIndex = Array.FindIndex(memory, m => thread.StackLength > 0 && thread.StackAddress >= m.Address && thread.StackAddress < (ulong)m.Address + (uint)m.Bytes.Length);
                var stackRva = stackIndex < 0 ? 0u : checked(memoryOffsets[stackIndex] + thread.StackAddress - memory[stackIndex].Address);
                if (thread.StackLength > 0 && stackIndex < 0) throw new ArgumentException("Thread stack is not completely captured.", nameof(threads));
                if (stackIndex >= 0)
                {
                    var covered = (ulong)memory[stackIndex].Address + (uint)memory[stackIndex].Bytes.Length;
                    for (var j = stackIndex + 1; covered < (ulong)thread.StackAddress + thread.StackLength && j < memory.Length && memory[j].Address == covered; j++) covered += (uint)memory[j].Bytes.Length;
                    if (covered < (ulong)thread.StackAddress + thread.StackLength) throw new ArgumentException("Thread stack is not completely captured.", nameof(threads));
                }
                writer.Write(thread.ThreadId); writer.Write(thread.SuspendCount); writer.Write(0u); writer.Write(thread.Priority); writer.Write(thread.Teb); writer.Write((ulong)thread.StackAddress); writer.Write(thread.StackLength); writer.Write(stackRva); writer.Write((uint)thread.Context.Length); writer.Write(contextOffsets[i]);
            }
            if (fullMemory) { writer.Write((ulong)memory.Length); writer.Write((ulong)(memory.Length > 0 ? memoryOffsets[0] : cursor)); }
            else writer.Write((uint)memory.Length);
            for (var i = 0; i < memory.Length; i++) { writer.Write((ulong)memory[i].Address); if (fullMemory) writer.Write((ulong)memory[i].Bytes.Length); else { writer.Write((uint)memory[i].Bytes.Length); writer.Write(memoryOffsets[i]); } }
            // Native Xbox callbacks 0x4718F8 / 0x47198C supply fixed Xenon platform metadata.
            writer.Write((ushort)3); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write((byte)2); writer.Write((byte)1);
            writer.Write(5u); writer.Write(1u); writer.Write(21256u); writer.Write(2u); writer.Write(payloadRva); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(new byte[24]);
            writer.Write(24u); writer.Write(0u); writer.Write(new byte[16]);
            if (exception is not null)
            {
                writer.Write(exception.ThreadId); writer.Write(0u); writer.Write(exception.Code); writer.Write((uint)(exception.Flags & ExceptionFlags.Noncontinuable)); writer.Write(0UL); writer.Write((ulong)exception.Address); writer.Write(exception.ParameterCount); writer.Write(0u);
                for (var i = 0; i < 15; i++) writer.Write(i < exception.Parameters.Count ? (ulong)exception.Parameters[i] : 0UL);
                var index = Array.FindIndex(threads, t => t.ThreadId == exception.ThreadId);
                writer.Write((uint)threads[index].Context.Length); writer.Write(contextOffsets[index]);
            }
            writer.Write((uint)version.Length); writer.Write(version); writer.Write((ushort)0);
            foreach (var name in names) { writer.Write((uint)name.Length); writer.Write(name); }
            foreach (var thread in threads) writer.Write(thread.Context);
            foreach (var range in memory) writer.Write(range.Bytes);
            writer.Flush(); output.Flush(true);
            void Directory(uint type, uint length, uint rva) { writer.Write(type); writer.Write(length); writer.Write(rva); }
        }
        catch { output.Dispose(); File.Delete(path); throw; }
    }
    internal static DumpFlags NormalizeFlags(DumpFlags flags)
    {
        if (((uint)flags & ~0x3ffu) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        if (flags.HasFlag(DumpFlags.WithFullMemory)) flags = (DumpFlags)((uint)flags & 0xfffffca6u);
        // Original Xbox callback at 0x40C210+304 is E_NOTIMPL: handle enumeration cannot produce a dump.
        if (flags.HasFlag(DumpFlags.WithHandleData)) throw new NotSupportedException("The Xbox monitor does not expose handle enumeration.");
        return flags;
    }
}
