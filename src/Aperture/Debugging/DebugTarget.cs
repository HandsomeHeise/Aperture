using System.Text;
using Aperture.Memory;
using Aperture.Models;
using Aperture.Transport;
using Aperture.Symbols;

namespace Aperture.Debugging;

public sealed record ModuleInfo(string Name, uint BaseAddress, uint Size, uint Checksum, uint Timestamp, uint PDataAddress, uint PDataSize, uint OriginalSize)
{
    public ModuleInfoFlags Flags { get; init; }
    public string FullName { get; init; } = "";
}
public sealed record SectionInfo(string Name, uint BaseAddress, uint Size, uint Index, uint Flags);
public sealed record ThreadInfo(uint ThreadId, uint SuspendCount, uint Priority, uint TlsBase, uint StartAddress, uint StackBase, uint StackLimit, uint StackSlackSpace, DateTimeOffset? CreateTime, string Name, uint CurrentProcessor, uint LastError);
public sealed record MemoryRegion(uint BaseAddress, uint Size, uint Protection, uint PhysicalAddress);

public sealed class DebugTarget(ConsoleConnection connection, ConsoleMemory? sharedMemory = null, XboxConsole? console = null)
{
    public XboxConsole? Console { get; } = console;
    public Aperture.Consoles.ConsoleManager? Manager => Console?.Manager;
    public string Name => Console?.Name ?? connection.Host;
    public Task<RunningProcess> GetRunningProcessAsync(CancellationToken cancellationToken = default) => Console?.GetRunningProcessAsync(cancellationToken) ?? GetStandaloneRunningProcessAsync(cancellationToken);
    private async Task<RunningProcess> GetStandaloneRunningProcessAsync(CancellationToken cancellationToken)
    {
        var pid = ProtocolFields.Parse((await connection.ExecuteAsync("getpid", cancellationToken).ConfigureAwait(false)).Message).GetUInt32("pid");
        var response = await connection.ExecuteAsync("xbeinfo running", cancellationToken).ConfigureAwait(false);
        return new(pid, ProtocolFields.Parse(response.StatusCode == 202 ? string.Join(' ', response.Lines) : response.Message).GetString("name"));
    }
    public ConsoleMemory Memory { get; } = sharedMemory ?? new(connection);
    public bool IsDump => false;
    public ExecutableClient GetExecutable(string module) => new(connection, module);
    public ConsoleThread GetThread(uint threadId) => new(this, threadId);
    public ConsoleModule GetModule(ModuleInfo info) => new(this, info);
    public Task WriteDumpAsync(string path, DumpFlags flags = DumpFlags.Normal, CancellationToken cancellationToken = default) => new Aperture.Dumps.DumpCapture(this).WriteAsync(path, flags, cancellationToken);
    public async Task<string> GetModuleFullNameAsync(string module, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("xbeinfo name=" + ProtocolFields.Quote(module), cancellationToken).ConfigureAwait(false);
        return ProtocolFields.Parse(response.StatusCode == 202 ? string.Join(' ', response.Lines) : response.Message).GetString("name");
    }
    public async Task<StackFrame> GetTopOfStackAsync(uint threadId, CancellationToken cancellationToken = default)
    {
        var registers = await GetRegistersAsync(threadId, cancellationToken).ConfigureAwait(false);
        var modules = await GetModulesAsync(cancellationToken).ConfigureAwait(false);
        async Task<FunctionInfo?> Lookup(uint pc, CancellationToken ct)
        {
            var module = modules.FirstOrDefault(m => pc >= m.BaseAddress && (ulong)pc < (ulong)m.BaseAddress + m.Size);
            return module is null ? null : await GetFunctionInfoAsync(module, pc, ct).ConfigureAwait(false);
        }
        return new(registers, await Lookup(registers.Get32(Register32.iar), cancellationToken).ConfigureAwait(false), Memory.ReadAsync, Lookup, (context, ct) => context.FlushAsync(connection, threadId, ct));
    }
    public async Task<FunctionInfo?> GetFunctionInfoAsync(ModuleInfo module, uint address, CancellationToken cancellationToken = default)
    {
        var end = (ulong)module.BaseAddress + module.Size;
        if (module.Size == 0 || end > uint.MaxValue) throw new InvalidDataException("The module range cannot be represented by Xenon function metadata.");
        if (address < module.BaseAddress || (ulong)address >= end) throw new ArgumentOutOfRangeException(nameof(address), "The address must lie within the module.");
        cancellationToken.ThrowIfCancellationRequested();
        var noPData = new FunctionInfo(FunctionType.NoPData, module.BaseAddress, module.BaseAddress, (uint)end);
        if (module.PDataSize == 0) return noPData;
        if (module.PDataSize > 64 * 1024 * 1024) throw new InvalidDataException("Runtime function table exceeds limits.");
        return new FunctionTable(await Memory.ReadAsync(module.PDataAddress, (int)module.PDataSize, cancellationToken).ConfigureAwait(false)).Find(address) ?? noPData;
    }
    public async Task<IReadOnlyList<ModuleInfo>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("modules", cancellationToken).ConfigureAwait(false);
        return Records(response).Select(line =>
        {
            var f = ProtocolFields.Parse(line);
            return new ModuleInfo(f.GetString("name"), f.GetUInt32("base"), f.GetUInt32("size"), f.GetUInt32("check"), f.GetUInt32("timestamp"), f.GetUInt32("pdata"), f.GetUInt32("psize"), f.GetUInt32("osize"))
            {
                Flags = (f.ContainsKey("xbe") ? ModuleInfoFlags.Main : 0) | (f.ContainsKey("tls") ? ModuleInfoFlags.Tls : 0) | (f.ContainsKey("dll") ? ModuleInfoFlags.Dll : 0)
            };
        }).ToArray();
    }
    public async Task<IReadOnlyList<SectionInfo>> GetSectionsAsync(string module, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("modsections name=" + ProtocolFields.Quote(module), cancellationToken).ConfigureAwait(false);
        return Records(response).Select(line =>
        {
            var f = ProtocolFields.Parse(line);
            return new SectionInfo(f.GetString("name"), f.GetUInt32("base"), f.GetUInt32("size"), f.GetUInt32("index"), f.GetUInt32("flags"));
        }).ToArray();
    }
    public async Task<IReadOnlyList<uint>> GetThreadIdsAsync(CancellationToken cancellationToken = default) => Records(await connection.ExecuteAsync("threads", cancellationToken).ConfigureAwait(false)).Select(line => ProtocolFields.UInt32(line.Trim())).ToArray();
    public async Task<ThreadInfo> GetThreadInfoAsync(uint threadId, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync(FormattableString.Invariant($"threadinfo thread=0x{threadId:X8}"), cancellationToken).ConfigureAwait(false);
        var f = ProtocolFields.Parse(string.Join(' ', Records(response)));
        var name = f.GetString("name"); var nameLength = f.GetUInt32("namelen"); var nameAddress = f.GetUInt32("nameaddr");
        if (name.Length == 0 && nameAddress != 0 && nameLength > 0 && nameLength <= 4096) name = await Memory.ReadStringAsync(nameAddress, (int)nameLength, Encoding.Latin1, cancellationToken: cancellationToken).ConfigureAwait(false);
        DateTimeOffset? created = f.ContainsKey("createlo") ? DateTimeOffset.FromFileTime(checked((long)(((ulong)f.GetUInt32("createhi") << 32) | f.GetUInt32("createlo")))) : null;
        return new(threadId, f.GetUInt32("suspend"), f.GetUInt32("priority"), f.GetUInt32("tlsbase"), f.GetUInt32("start"), f.GetUInt32("base"), f.GetUInt32("limit"), f.GetUInt32("slack"), created, name, f.GetUInt32("proc"), f.GetUInt32("lasterr"));
    }
    public async Task<IReadOnlyList<MemoryRegion>> GetMemoryRegionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("walkmem", cancellationToken).ConfigureAwait(false);
        return Records(response).Select(line =>
        {
            var f = ProtocolFields.Parse(line);
            return new MemoryRegion(f.GetUInt32("base"), f.GetUInt32("size"), f.GetUInt32("protect"), f.GetUInt32("phys"));
        }).ToArray();
    }
    public async Task<RegisterSet> GetRegistersAsync(uint threadId, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync(FormattableString.Invariant($"getcontext thread=0x{threadId:X8} control int fp vr"), cancellationToken).ConfigureAwait(false);
        return RegisterSet.Parse(Records(response).ToArray());
    }
    public async Task StopAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("stop", cancellationToken).ConfigureAwait(false);
    public async Task GoAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("go", cancellationToken).ConfigureAwait(false);
    /// <returns>True when execution was already stopped; false when this operation stopped it.</returns>
    public async Task<bool> StopWithStatusAsync(CancellationToken cancellationToken = default)
    {
        try { await StopAsync(cancellationToken).ConfigureAwait(false); return false; }
        catch (ApertureProtocolException error) when (error.StatusCode == 426) { return true; }
    }
    /// <returns>True when execution was not stopped; false when this operation resumed it.</returns>
    public async Task<bool> GoWithStatusAsync(CancellationToken cancellationToken = default)
    {
        try { await GoAsync(cancellationToken).ConfigureAwait(false); return false; }
        catch (ApertureProtocolException error) when (error.StatusCode == 408) { return true; }
    }
    public async Task HaltThreadAsync(uint threadId, CancellationToken cancellationToken = default) => _ = await ThreadCommand("halt", threadId, cancellationToken).ConfigureAwait(false);
    public async Task ContinueThreadAsync(uint threadId, bool exception = false, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"continue thread=0x{threadId:X8}{(exception ? " exception" : "")}"), cancellationToken).ConfigureAwait(false);
    public async Task SuspendThreadAsync(uint threadId, CancellationToken cancellationToken = default) => _ = await ThreadCommand("suspend", threadId, cancellationToken).ConfigureAwait(false);
    public async Task ResumeThreadAsync(uint threadId, CancellationToken cancellationToken = default) => _ = await ThreadCommand("resume", threadId, cancellationToken).ConfigureAwait(false);
    public async Task<ProtocolResponse> GetStopEventAsync(uint threadId, CancellationToken cancellationToken = default) => await ThreadCommand("isstopped", threadId, cancellationToken).ConfigureAwait(false);
    public async Task SetBreakpointAsync(uint address, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"break addr=0x{address:X8}"), cancellationToken).ConfigureAwait(false);
    public async Task RemoveBreakpointAsync(uint address, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"break addr=0x{address:X8} clear"), cancellationToken).ConfigureAwait(false);
    public async Task RemoveAllBreakpointsAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("break clearall", cancellationToken).ConfigureAwait(false);
    public async Task SetInitialBreakpointAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("break start", cancellationToken).ConfigureAwait(false);
    public async Task SetDataBreakpointAsync(uint address, BreakpointType type, uint size, CancellationToken cancellationToken = default)
    {
        if (size is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(size));
        var kind = type switch { BreakpointType.OnWrite => "write", BreakpointType.OnRead => "readwrite", BreakpointType.OnExecuteHW => throw new NotSupportedException("This SDK protocol does not implement hardware execution breakpoints."), _ => throw new ArgumentOutOfRangeException(nameof(type)) };
        _ = await connection.ExecuteAsync(FormattableString.Invariant($"break {kind}=0x{address:X8} size={size}"), cancellationToken).ConfigureAwait(false);
    }
    public async Task<BreakpointType> GetBreakpointTypeAsync(uint address, CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await connection.ExecuteAsync(FormattableString.Invariant($"isbreak addr=0x{address:X8}"), cancellationToken).ConfigureAwait(false)).Message);
        var type = fields.GetUInt32("type");
        return type switch { 0 => BreakpointType.NoBreakpoint, 1 => BreakpointType.OnWrite, 2 => BreakpointType.OnRead, 3 => BreakpointType.OnExecuteHW, 4 => BreakpointType.OnExecute, 5 => BreakpointType.OnRead, _ => throw new InvalidDataException("Unknown breakpoint type.") };
    }
    public async Task StopOnAsync(StopOnFlags flags, bool enable, CancellationToken cancellationToken = default)
    {
        if (((int)flags & ~127) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
        var names = new List<string>();
        if (flags.HasFlag(StopOnFlags.OnThreadCreate)) names.Add("createthread");
        if (flags.HasFlag(StopOnFlags.OnFirstChanceException)) names.Add("fce");
        if (flags.HasFlag(StopOnFlags.OnDebugString)) names.Add("debugstr");
        if (flags.HasFlag(StopOnFlags.OnStackTrace)) names.Add("stacktrace");
        if (flags.HasFlag(StopOnFlags.OnModuleLoad)) names.Add("modload");
        // Native DmStopOn (RVA 0x3EA38) ignores OnTitleLaunch; it has no wire token.
        if (flags.HasFlag(StopOnFlags.OnPgoModuleStartup)) names.Add("pogomodstart");
        _ = await connection.ExecuteAsync((enable ? "stopon " : "nostopon ") + string.Join(' ', names), cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyDictionary<string, string>> GetDebuggerAsync(CancellationToken cancellationToken = default) => ProtocolFields.Parse((await connection.ExecuteAsync("isdebugger", cancellationToken).ConfigureAwait(false)).Message);
    public async Task ConnectAsDebuggerAsync(string name, bool force = false, bool monitorOnly = false, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("debugger connect name=" + ProtocolFields.Quote(name) + (force ? " force" : "") + (monitorOnly ? " monitor" : ""), cancellationToken).ConfigureAwait(false);
    public async Task DisconnectAsDebuggerAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("debugger disconnect", cancellationToken).ConfigureAwait(false);
    public async Task StartFileEventCaptureAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("fileevent advise", cancellationToken).ConfigureAwait(false);
    public async Task StopFileEventCaptureAsync(CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("fileevent unadvise", cancellationToken).ConfigureAwait(false);
    public async Task<uint> LoadDebuggerExtensionAsync(string name, CancellationToken cancellationToken = default)
    {
        var f = ProtocolFields.Parse((await connection.ExecuteAsync("dbgextld name=" + ProtocolFields.Quote(name), cancellationToken).ConfigureAwait(false)).Message);
        return f.GetUInt32("module");
    }
    public async Task UnloadDebuggerExtensionAsync(uint module, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"dbgextld unload module=0x{module:X8}"), cancellationToken).ConfigureAwait(false);
    public async Task PgoStartDataCollectionAsync(uint module, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"pogoctrl start module=0x{module:X}"), cancellationToken).ConfigureAwait(false);
    public async Task PgoStopDataCollectionAsync(uint module, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"pogoctrl stop module=0x{module:X}"), cancellationToken).ConfigureAwait(false);
    public async Task PgoSaveSnapshotAsync(string phase, bool reset, uint module, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"pogoctrl snapshot phase={ProtocolFields.Quote(phase)} reset={(reset ? 1 : 0)} module=0x{module:X}"), cancellationToken).ConfigureAwait(false);
    public async Task PgoSetAllocScaleAsync(uint module, uint scale, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"pogoctrl allocscale module=0x{module:X} scale={scale}"), cancellationToken).ConfigureAwait(false);
    private Task<ProtocolResponse> ThreadCommand(string command, uint threadId, CancellationToken ct) => connection.ExecuteAsync(FormattableString.Invariant($"{command} thread=0x{threadId:X8}"), ct);
    private static IReadOnlyList<string> Records(ProtocolResponse response) => response.StatusCode == 202 ? response.Lines : throw new InvalidDataException("Expected multiline debug records.");
}
