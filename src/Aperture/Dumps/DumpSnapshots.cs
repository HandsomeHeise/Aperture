namespace Aperture.Dumps;

public sealed record DumpThreadSnapshot(uint ThreadId, uint SuspendCount, uint Priority, ulong Teb, byte[] Context)
{
    public uint StackAddress { get; init; }
    public uint StackLength { get; init; }
}
public sealed record DumpMemorySnapshot(uint Address, byte[] Bytes)
{
    public bool Filterable { get; init; } = true;
}
