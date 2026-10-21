using Aperture.Models;
using Aperture.Transport;

namespace Aperture.Debugging;

public sealed class StackFrame
{
    private readonly Func<uint, int, CancellationToken, Task<byte[]>> readMemory;
    private readonly Func<uint, CancellationToken, Task<FunctionInfo?>> lookupFunction;
    private readonly Func<RegisterSet, CancellationToken, Task>? flush;
    private readonly HashSet<(uint Pc, ulong Sp)> visited;
    public RegisterSet Registers { get; }
    public bool TopOfStack { get; }
    public bool Dirty => Registers.Dirty;
    public uint InstructionPointer => Registers.Get32(Register32.iar);
    public uint StackPointer => checked((uint)Registers.Get64(Register64.r1));
    public FunctionInfo? FunctionInfo { get; }
    internal StackFrame(RegisterSet registers, FunctionInfo? functionInfo, Func<uint, int, CancellationToken, Task<byte[]>> readMemory, Func<uint, CancellationToken, Task<FunctionInfo?>> lookupFunction, Func<RegisterSet, CancellationToken, Task>? flush, bool topOfStack = true, HashSet<(uint Pc, ulong Sp)>? visited = null)
    {
        Registers = registers; FunctionInfo = functionInfo; this.readMemory = readMemory; this.lookupFunction = lookupFunction; this.flush = flush; TopOfStack = topOfStack;
        this.visited = visited is null ? [] : new(visited);
        if (!this.visited.Add((InstructionPointer, StackPointer))) throw new InvalidDataException("Cyclic stack frame.");
        if (this.visited.Count > 1024) throw new InvalidDataException("Stack frame limit exceeded.");
    }
    public async Task<StackFrame?> GetNextStackFrameAsync(CancellationToken cancellationToken = default)
    {
        var context = await PowerPcUnwinder.UnwindAsync(Registers, FunctionInfo, readMemory, cancellationToken, lookupFunction).ConfigureAwait(false);
        if (context is null) return null;
        var pc = context.Get32(Register32.iar);
        var function = await lookupFunction(pc >= 4 ? pc - 4 : pc, cancellationToken).ConfigureAwait(false);
        return new(context, function, readMemory, lookupFunction, null, false, visited);
    }
    public async Task<uint> GetReturnAddressAsync(CancellationToken cancellationToken = default) => (await GetNextStackFrameAsync(cancellationToken).ConfigureAwait(false))?.InstructionPointer ?? 0;
    public Task FlushRegisterChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!TopOfStack || flush is null) throw new InvalidOperationException("Only a live top frame can flush registers.");
        return flush(Registers, cancellationToken);
    }
}

public sealed class ConsoleThread(DebugTarget target, uint threadId)
{
    public uint ThreadId => threadId;
    public Task<ThreadInfo> GetInfoAsync(CancellationToken cancellationToken = default) => target.GetThreadInfoAsync(threadId, cancellationToken);
    public Task<StackFrame> GetTopOfStackAsync(CancellationToken cancellationToken = default) => target.GetTopOfStackAsync(threadId, cancellationToken);
    public Task<ProtocolResponse> GetStopEventAsync(CancellationToken cancellationToken = default) => target.GetStopEventAsync(threadId, cancellationToken);
    public Task<DebugEventInfo> GetStopEventInfoAsync(CancellationToken cancellationToken = default) => target.GetStopEventInfoAsync(threadId, cancellationToken);
    public Task HaltAsync(CancellationToken cancellationToken = default) => target.HaltThreadAsync(threadId, cancellationToken);
    public Task ContinueAsync(bool exception = false, CancellationToken cancellationToken = default) => target.ContinueThreadAsync(threadId, exception, cancellationToken);
    public Task SuspendAsync(CancellationToken cancellationToken = default) => target.SuspendThreadAsync(threadId, cancellationToken);
    public Task ResumeAsync(CancellationToken cancellationToken = default) => target.ResumeThreadAsync(threadId, cancellationToken);
}

public sealed class ConsoleModule(DebugTarget target, ModuleInfo info)
{
    public ModuleInfo Info => info;
    public async Task<ModuleInfo> GetInfoAsync(CancellationToken cancellationToken = default) => info with { FullName = await GetFullNameAsync(cancellationToken).ConfigureAwait(false) };
    public uint OriginalSize => info.OriginalSize;
    public Aperture.Symbols.ExecutableClient Executable => target.GetExecutable(info.Name);
    public Task<IReadOnlyList<SectionInfo>> GetSectionsAsync(CancellationToken cancellationToken = default) => target.GetSectionsAsync(info.Name, cancellationToken);
    public Task<FunctionInfo?> GetFunctionInfoAsync(uint address, CancellationToken cancellationToken = default) => target.GetFunctionInfoAsync(info, address, cancellationToken);
    public Task<uint> GetEntryPointAddressAsync(CancellationToken cancellationToken = default) => Executable.GetEntryPointAddressAsync(cancellationToken);
    public Task<string> GetFullNameAsync(CancellationToken cancellationToken = default) => target.GetModuleFullNameAsync(info.Name, cancellationToken);
}
