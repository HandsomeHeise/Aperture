using Aperture.Models;
using Aperture.Transport;
namespace Aperture.Debugging;

/// <summary>Managed projection of XBOX_EVENT_INFO. Descriptors are snapshots of the notification.</summary>
public sealed record DebugEventInfo(DebugEventType Event, bool IsThreadStopped, uint ThreadId, ModuleInfo? Module, SectionInfo? Section, ExecutionState? ExecState, string Message, uint Code, uint Address, ExceptionFlags Flags, IReadOnlyList<uint> Parameters)
{
    public uint ParameterCount { get; init; } = (uint)Parameters.Count;
    public uint FiberId { get; init; }
    public uint FiberStartAddress { get; init; }
    /// <summary>The fixed fifteen DWORD slots of XBOX_EVENT_INFO, including native fiber slots outside ParameterCount.</summary>
    public IReadOnlyList<uint> ParameterSlots => Array.AsReadOnly(Enumerable.Range(0, 15).Select(i =>
        Event is DebugEventType.FiberCreate or DebugEventType.FiberDestroy ? i == 0 ? FiberId : i == 1 ? FiberStartAddress : 0u : i < Parameters.Count ? Parameters[i] : 0u).ToArray());
    public DebugEventInfo Snapshot() => this with { Parameters = Array.AsReadOnly(Parameters.ToArray()) };
    public ConsoleThread GetThread(DebugTarget target) => new(target, ThreadId);
    internal static DebugEventInfo FromNotification(DebugNotification n, IReadOnlyList<uint>? parameters = null)
    {
        var f = n.Fields;
        if (n.Type == DebugEventType.AssertionFailed && f.GetUInt32("strlen") > 0) throw new InvalidOperationException("Assertion message refers to console memory; use GetEventInfoAsync to resolve it.");
        if (n.Type == DebugEventType.Exception && f.GetUInt32("code") == 0xC0000005)
            parameters = Array.AsReadOnly(new[] { f.GetUInt32("nparam", f.ContainsKey("write") ? 1u : 0u), f.GetUInt32("params", f.GetUInt32("write", f.GetUInt32("read"))) });
        else if (n.Type == DebugEventType.Exception && f.GetUInt32("nparam") > 0 && parameters is null)
            throw new InvalidOperationException("Event parameters refer to console memory; use GetEventInfoAsync to resolve them.");
        if (n.Type == DebugEventType.DataBreak)
        {
            var kind = f.ContainsKey("write") ? 1u : f.ContainsKey("read") ? 2u : f.ContainsKey("execute") ? 3u : 0u;
            var dataAddress = kind switch { 1 => f.GetUInt32("write"), 2 => f.GetUInt32("read"), 3 => f.GetUInt32("execute"), _ => 0u };
            parameters = Array.AsReadOnly(new[] { kind, dataAddress });
        }
        if (n.Type == DebugEventType.PgoModuleStartup) parameters = Array.AsReadOnly(new[] { f.GetUInt32("module") });
        if (n.Type is DebugEventType.FiberCreate or DebugEventType.FiberDestroy) parameters = Array.AsReadOnly(new[] { f.GetUInt32("id"), f.GetUInt32("start") });
        ExecutionState? state = n.Type == DebugEventType.ExecStateChange ?
            f.ContainsKey("started") ? ExecutionState.Running : f.ContainsKey("stopped") ? ExecutionState.Stopped :
            f.ContainsKey("pending") ? ExecutionState.Pending : f.ContainsKey("pending_title") ? ExecutionState.PendingTitle :
            f.ContainsKey("reboot_title") ? ExecutionState.RebootingTitle : f.ContainsKey("rebooting") ? ExecutionState.Rebooting : null : null;
        var flags = (f.ContainsKey("first") ? ExceptionFlags.FirstChance : 0) | (f.ContainsKey("noncont") ? ExceptionFlags.Noncontinuable : 0);
        ModuleInfo? module = n.Type is DebugEventType.ModuleLoad or DebugEventType.ModuleUnload ?
            new(f.GetString("name"), f.GetUInt32("base"), f.GetUInt32("size"), f.GetUInt32("checksum", f.GetUInt32("check")), f.GetUInt32("timestamp"), f.GetUInt32("pdata"), f.GetUInt32("psize"), f.GetUInt32("originalsize", f.GetUInt32("size"))) { Flags = (ModuleInfoFlags)((f.ContainsKey("xbe") ? 1 : 0) | (f.ContainsKey("tls") ? 2 : 0) | (f.ContainsKey("dll") ? 4 : 0)) } : null;
        SectionInfo? section = n.Type is DebugEventType.SectionLoad or DebugEventType.SectionUnload ?
            new(f.GetString("name"), f.GetUInt32("base"), f.GetUInt32("size"), f.GetUInt32("index"), f.GetUInt32("flags")) : null;
        var message = n.Type is DebugEventType.DebugString or DebugEventType.AssertionFailed or DebugEventType.AssertionFailedEx or DebugEventType.RIP or DebugEventType.NoEvent ? n.Text : "";
        var address = n.Type == DebugEventType.ThreadCreate ? f.GetUInt32("start", n.Address) : n.Address;
        return new(n.Type, f.ContainsKey("stop"), n.ThreadId, module, section, state, message, f.GetUInt32("code"), address, flags, parameters ?? Array.Empty<uint>()) { FiberId = f.GetUInt32("id"), FiberStartAddress = f.GetUInt32("start"), ParameterCount = n.Type is DebugEventType.FiberCreate or DebugEventType.FiberDestroy ? 0u : (uint)(parameters?.Count ?? 0) };
    }
}



public static class DebugEventProjection
{
    public static async Task<DebugEventInfo> GetStopEventInfoAsync(this DebugTarget target, uint threadId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var response = await target.GetStopEventAsync(threadId, cancellationToken).ConfigureAwait(false);
        var notification = DebugNotification.Parse(response.Message);
        var result = await notification.GetEventInfoAsync(target, cancellationToken).ConfigureAwait(false);
        return result with { ThreadId = threadId, IsThreadStopped = true };
    }

    public static async Task<DebugEventInfo> GetEventInfoAsync(this DebugNotification notification, DebugTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(target);
        if (notification.Type == DebugEventType.AssertionFailed && notification.Fields.GetUInt32("strlen") > 0)
        {
            var length = notification.Fields.GetUInt32("strlen");
            if (length > 1024 * 1024) throw new InvalidDataException("Assertion message exceeds the payload limit.");
            var data = await target.Memory.ReadAsync(notification.Fields.GetUInt32("straddr"), (int)length, cancellationToken).ConfigureAwait(false);
            var fields = new Dictionary<string, string>(notification.Fields, StringComparer.OrdinalIgnoreCase);
            fields.Remove("strlen");
            return DebugEventInfo.FromNotification(notification with { Text = System.Text.Encoding.Latin1.GetString(data), Fields = fields });
        }
        if (notification.Type == DebugEventType.Exception && notification.Fields.GetUInt32("code") == 0xC0000005) return notification.EventInfo;
        var count = notification.Fields.GetUInt32("nparam");
        if (count > 15) throw new InvalidDataException("Exception parameter count exceeds XBOX_EVENT_INFO capacity.");
        if (count == 0) return notification.EventInfo;
        var address = notification.Fields.GetUInt32("params");
        if (address == 0) throw new InvalidDataException("Exception parameter memory address is absent.");
        // The native notification parser retains a console address in Information[1].
        var bytes = await target.Memory.ReadAsync(address, checked((int)count * 4), cancellationToken).ConfigureAwait(false);
        var parameters = new uint[count];
        for (var i = 0; i < parameters.Length; i++) parameters[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i * 4, 4));
        return DebugEventInfo.FromNotification(notification, Array.AsReadOnly(parameters));
    }
}







