using Aperture.Debugging;
using Aperture.Models;

namespace Aperture.Dumps;

public sealed record MemoryRange(uint Address, uint Length);
/// <summary>Captures selected live ranges and thread contexts without changing target execution state.</summary>
/// <remarks>Stop the target first when a coherent process snapshot is required. An unreadable range aborts capture.</remarks>
public sealed class DumpCapture(DebugTarget target)
{
    public ulong MaximumBytes { get; set; } = 128 * 1024 * 1024;
    public async Task WriteAsync(string path, MemoryRange[]? selectedMemory = null, bool includeStacks = true, CancellationToken cancellationToken = default)
        => await WriteCoreAsync(path, selectedMemory, includeStacks, 0, cancellationToken).ConfigureAwait(false);
    private async Task WriteCoreAsync(string path, MemoryRange[]? selectedMemory, bool includeStacks, DumpFlags flags, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) throw new IOException("Dump output already exists.");
        var ranges = selectedMemory is null ? new List<MemoryRange>() : new List<MemoryRange>(selectedMemory);
        var modules = await target.GetModulesAsync(cancellationToken).ConfigureAwait(false);
        modules = await Task.WhenAll(modules.Select(async module =>
        {
            if (module.FullName.Length > 0) return module;
            try { return module with { FullName = await target.GetModuleFullNameAsync(module.Name, cancellationToken).ConfigureAwait(false) }; }
            catch (Aperture.Transport.ApertureProtocolException ex) when (ex.StatusCode == 407) { return module; }
        })).ConfigureAwait(false);
        var threadIds = await target.GetThreadIdsAsync(cancellationToken).ConfigureAwait(false);
        var threads = new List<DumpThreadSnapshot>();
        DebugEventInfo? stopEvent = null;
        foreach (var id in threadIds)
        {
            var info = await target.GetThreadInfoAsync(id, cancellationToken).ConfigureAwait(false);
            var context = await target.GetRegistersAsync(id, cancellationToken).ConfigureAwait(false);
            var stack = checked((uint)context.Get64(Register64.r1));
            if (includeStacks && (stack < info.StackLimit || stack > info.StackBase)) throw new InvalidDataException("Thread stack pointer is outside its allocation.");
            if (includeStacks) stack = Math.Max(info.StackLimit, stack >= info.StackSlackSpace ? stack - info.StackSlackSpace : 0);
            threads.Add(new(id, info.SuspendCount, info.Priority, info.TlsBase, XenonContextCodec.Encode(context))
            { StackAddress = includeStacks ? stack : 0, StackLength = includeStacks && stack <= info.StackBase ? info.StackBase - stack : 0 });
            if (stopEvent is null)
            {
                try
                {
                    var candidate = await target.GetStopEventInfoAsync(id, cancellationToken).ConfigureAwait(false);
                    if (candidate.Event == DebugEventType.Exception) stopEvent = candidate;
                    else if (candidate.Event is DebugEventType.ExecutionBreak or DebugEventType.SingleStep or DebugEventType.RIP)
                        stopEvent = candidate with { Code = 0x80000003, Parameters = candidate.Event == DebugEventType.RIP ? Array.AsReadOnly(new uint[] { 27 }) : Array.Empty<uint>() };
                    else if (candidate.Event == DebugEventType.AssertionFailedEx) stopEvent = candidate with { Code = 0xC0000420 };
                }
                catch (Aperture.Transport.ApertureProtocolException ex) when (ex.StatusCode == 408) { }
            }
            if (includeStacks)
            {
                if (stack < info.StackBase) ranges.Add(new(stack, info.StackBase - stack));
            }
        }
        ranges = Normalize(ranges);
        if (ranges.Aggregate(0UL, (size, range) => checked(size + range.Length)) > MaximumBytes) throw new IOException("Selected dump memory exceeds the configured capture limit.");
        target.Memory.InvalidateCache();
        var memory = new List<DumpMemorySnapshot>();
        foreach (var range in ranges)
        {
            // Small independent descriptors bound allocation and retain a streaming-compatible file layout.
            ulong offset = 0;
            while (offset < range.Length)
            {
                var length = (int)Math.Min(1024 * 1024UL, range.Length - offset);
                var address = checked((uint)((ulong)range.Address + offset));
                memory.Add(new(address, await target.Memory.ReadAsync(address, length, cancellationToken).ConfigureAwait(false)));
                offset += (uint)length;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (flags.HasFlag(DumpFlags.WithIndirectlyReferencedMemory))
        {
            var regions = await target.GetMemoryRegionsAsync(cancellationToken).ConfigureAwait(false);
            var indirect = new List<MemoryRange>();
            foreach (var range in memory)
                for (var offset = 0; offset + 4 <= range.Bytes.Length; offset += 4)
                {
                    var location = (ulong)range.Address + (uint)offset;
                    if (!threads.Any(t => location >= t.StackAddress && location < (ulong)t.StackAddress + t.StackLength)) continue;
                    // Native xbdm 0x474784 treats each aligned word as a host-order pointer.
                    var pointer = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(range.Bytes.AsSpan(offset, 4));
                    if (modules.Any(m => pointer >= m.BaseAddress && (ulong)pointer < (ulong)m.BaseAddress + m.Size)) continue;
                    var start = pointer >= 256 ? pointer - 256 : 0;
                    var end = Math.Min((ulong)start + 1024, 0x1_0000_0000UL);
                    foreach (var region in regions)
                    {
                        if ((region.Protection & 0x201) != 0) continue;
                        var begin = Math.Max((ulong)start, region.BaseAddress);
                        var stop = Math.Min(end, (ulong)region.BaseAddress + region.Size);
                        if (begin < stop) indirect.Add(new((uint)begin, (uint)(stop - begin)));
                    }
                }
            foreach (var range in Normalize(indirect))
            {
                var start = (ulong)range.Address; var stop = start + range.Length;
                foreach (var existing in memory.OrderBy(m => m.Address).ToArray())
                {
                    if ((ulong)existing.Address + (uint)existing.Bytes.Length <= start || existing.Address >= stop) continue;
                    if (existing.Address > start) await AddIndirect((uint)start, checked((uint)(existing.Address - start))).ConfigureAwait(false);
                    start = Math.Max(start, (ulong)existing.Address + (uint)existing.Bytes.Length);
                    if (start >= stop) break;
                }
                if (start < stop) await AddIndirect((uint)start, (uint)(stop - start)).ConfigureAwait(false);
            }
            async Task AddIndirect(uint address, uint length)
            {
                if (memory.Aggregate(0UL, (size, m) => size + (uint)m.Bytes.Length) + length > MaximumBytes) throw new IOException("Indirect dump memory exceeds the configured capture limit.");
                memory.Add(new(address, await target.Memory.ReadAsync(address, checked((int)length), cancellationToken).ConfigureAwait(false)));
            }
        }
        DumpWriter.WriteWithFlags(path, modules.ToArray(), threads.ToArray(), memory.ToArray(), flags, stopEvent);
    }
    public async Task WriteAsync(string path, DumpFlags flags, CancellationToken cancellationToken = default)
    {
        flags = DumpWriter.NormalizeFlags(flags);
        var ranges = new List<MemoryRange>();
        if (flags.HasFlag(DumpFlags.WithFullMemory) || flags.HasFlag(DumpFlags.WithPrivateReadWriteMemory))
        {
            foreach (var region in await target.GetMemoryRegionsAsync(cancellationToken).ConfigureAwait(false))
                if (region.BaseAddress < 0xA0000000 && (region.Protection & 0x201) == 0 && (flags.HasFlag(DumpFlags.WithFullMemory) || (region.Protection & 4) != 0))
                    ranges.Add(new(region.BaseAddress, (uint)Math.Min(region.Size, 0xA0000000UL - region.BaseAddress)));
        }
        if (flags.HasFlag(DumpFlags.WithDataSegs))
        {
            foreach (var module in await target.GetModulesAsync(cancellationToken).ConfigureAwait(false))
                foreach (var section in await target.GetSectionsAsync(module.Name, cancellationToken).ConfigureAwait(false))
                    if ((section.Flags & (uint)SectionInfoFlags.Writeable) != 0) ranges.Add(new(section.BaseAddress, section.Size));
        }
        await WriteCoreAsync(path, ranges.ToArray(), !flags.HasFlag(DumpFlags.WithFullMemory), flags, cancellationToken).ConfigureAwait(false);
    }
    private static List<MemoryRange> Normalize(List<MemoryRange> ranges)
    {
        var result = new List<MemoryRange>();
        foreach (var range in ranges.Where(r => r.Length > 0).OrderBy(r => r.Address))
        {
            var end = (ulong)range.Address + range.Length;
            if (end > 0x1_0000_0000UL) throw new ArgumentOutOfRangeException(nameof(ranges), "Memory range wraps Xbox address space.");
            if (result.Count == 0 || (ulong)result[^1].Address + result[^1].Length < range.Address) result.Add(range);
            else
            {
                var previous = result[^1]; var mergedEnd = Math.Max(end, (ulong)previous.Address + previous.Length);
                result[^1] = new(previous.Address, checked((uint)(mergedEnd - previous.Address)));
            }
        }
        return result;
    }
}
