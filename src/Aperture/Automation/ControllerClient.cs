using System.Buffers.Binary;
using Aperture.Transport;

namespace Aperture.Automation;

public sealed record GamepadQueueInfo(uint QueueLength, uint PacketCount, uint RemainingMilliseconds, uint RemainingCount);

public sealed class ControllerClient(ConsoleConnection connection)
{
    public async Task<bool> GetInputProcessAsync(uint user, CancellationToken cancellationToken = default)
    {
        var response = await Command(user, "process", cancellationToken).ConfigureAwait(false);
        return ProtocolFields.Parse(response.Message).ContainsKey("system");
    }
    public async Task BindAsync(uint user, uint queueLength, CancellationToken cancellationToken = default) => _ = await Command(user, FormattableString.Invariant($"bind queuelen={queueLength}"), cancellationToken).ConfigureAwait(false);
    public async Task UnbindAsync(uint user, CancellationToken cancellationToken = default) => _ = await Command(user, "unbind", cancellationToken).ConfigureAwait(false);
    public async Task ConnectAsync(uint user, CancellationToken cancellationToken = default) => _ = await Command(user, "connect", cancellationToken).ConfigureAwait(false);
    public async Task DisconnectAsync(uint user, CancellationToken cancellationToken = default) => _ = await Command(user, "disconnect", cancellationToken).ConfigureAwait(false);
    public async Task SetStateAsync(uint user, GamepadState state, CancellationToken cancellationToken = default)
    {
        ValidateUser(user); ArgumentNullException.ThrowIfNull(state);
        _ = await connection.SendBinaryAsync(FormattableString.Invariant($"autoinput user={user} setpacket"), state.ToWire(), cancellationToken).ConfigureAwait(false);
    }
    public async Task<uint> QueueAsync(uint user, IReadOnlyList<GamepadState> states, IReadOnlyList<uint>? durations = null, IReadOnlyList<uint>? counts = null, CancellationToken cancellationToken = default)
    {
        ValidateUser(user); ArgumentNullException.ThrowIfNull(states);
        if (states.Count == 0) return 0;
        if (durations is not null && durations.Count != states.Count || counts is not null && counts.Count != states.Count) throw new ArgumentException("Queue timing arrays must match the number of states.");
        var length = checked(states.Count * (12 + (durations is null ? 0 : 4) + (counts is null ? 0 : 4)));
        var packet = new byte[length];
        for (var i = 0; i < states.Count; i++) states[i].ToWire().CopyTo(packet, i * 12);
        var offset = states.Count * 12;
        if (durations is not null) foreach (var value in durations) { BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), value); offset += 4; }
        if (counts is not null) foreach (var value in counts) { BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), value); offset += 4; }
        var command = FormattableString.Invariant($"autoinput user={user} queuepackets count={states.Count}{(durations is null ? "" : " timearray")}{(counts is null ? "" : " countarray")}");
        var response = await connection.SendBinaryAsync(command, packet, cancellationToken).ConfigureAwait(false);
        var fields = ProtocolFields.Parse(response.Message);
        if (!fields.ContainsKey("queued")) throw new InvalidDataException("Queue response has no queued count.");
        return fields.GetUInt32("queued");
    }
    public Task<uint> QueueStateAsync(uint user, GamepadState state, uint duration, uint count, CancellationToken cancellationToken = default) => QueueAsync(user, [state], [duration], [count], cancellationToken);
    public async Task ClearQueueAsync(uint user, CancellationToken cancellationToken = default) => _ = await Command(user, "clearqueue", cancellationToken).ConfigureAwait(false);
    public async Task<GamepadQueueInfo> QueryQueueAsync(uint user, CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await Command(user, "queryqueue", cancellationToken).ConfigureAwait(false)).Message);
        return new(fields.GetUInt32("nQueue"), fields.GetUInt32("nPacket"), fields.GetUInt32("t_remain_ms"), fields.GetUInt32("t_remain_count"));
    }
    public async Task<ulong> GetDefaultProfileAsync(CancellationToken cancellationToken = default)
    {
        var fields = ProtocolFields.Parse((await connection.ExecuteAsync("autoprof", cancellationToken).ConfigureAwait(false)).Message);
        if (!fields.TryGetValue("xuid", out var value)) throw new InvalidDataException("Profile response has no XUID.");
        return ProtocolFields.UInt64(value);
    }
    public async Task SetDefaultProfileAsync(ulong xuid, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync(FormattableString.Invariant($"autoprof xuid=0q{xuid:X16}"), cancellationToken).ConfigureAwait(false);
    private Task<ProtocolResponse> Command(uint user, string action, CancellationToken ct)
    {
        ValidateUser(user);
        return connection.ExecuteAsync(FormattableString.Invariant($"autoinput user={user} {action}"), ct);
    }
    private static void ValidateUser(uint user) { if (user > 3) throw new ArgumentOutOfRangeException(nameof(user)); }
}
