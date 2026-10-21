using System.Globalization;
using Aperture.Transport;

namespace Aperture.Rpc;

public enum RpcThread { System, Title }
public enum LedState { Off = 0, Red = 8, Green = 128, Orange = 136 }
public enum TemperatureSensor { Cpu, Gpu, Edram, Motherboard }

public sealed class JrpcClient(ConsoleConnection connection)
{
    public const uint ProtocolVersion = 2;
    private readonly SemaphoreSlim calls = new(1, 1);
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public async Task<T> CallAsync<T>(uint address, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) where T : struct => ParseScalar<T>(await SendAsync(RpcWire.BuildCall(address, null, 0, ReturnType<T>(false), IsSystem(thread), 0, arguments), cancellationToken).ConfigureAwait(false));
    public async Task<T> CallAsync<T>(string module, uint ordinal, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) where T : struct => ParseScalar<T>(await SendAsync(RpcWire.BuildCall(0, module, ordinal, ReturnType<T>(false), IsSystem(thread), 0, arguments), cancellationToken).ConfigureAwait(false));
    public async Task<T[]> CallArrayAsync<T>(uint address, int count, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return [];
        return ParseArray<T>(await SendAsync(RpcWire.BuildCall(address, null, 0, ReturnType<T>(true), IsSystem(thread), (uint)count, arguments), cancellationToken).ConfigureAwait(false), count);
    }
    public async Task<T[]> CallArrayAsync<T>(string module, uint ordinal, int count, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return [];
        return ParseArray<T>(await SendAsync(RpcWire.BuildCall(0, module, ordinal, ReturnType<T>(true), IsSystem(thread), (uint)count, arguments), cancellationToken).ConfigureAwait(false), count);
    }
    public async Task CallVoidAsync(uint address, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) => _ = await SendAsync(RpcWire.BuildCall(address, null, 0, 0, IsSystem(thread), 0, arguments), cancellationToken).ConfigureAwait(false);
    public async Task CallVoidAsync(string module, uint ordinal, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) => _ = await SendAsync(RpcWire.BuildCall(0, module, ordinal, 0, IsSystem(thread), 0, arguments), cancellationToken).ConfigureAwait(false);
    public Task<string> CallStringAsync(uint address, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) => SendAsync(RpcWire.BuildCall(address, null, 0, 2, IsSystem(thread), 0, arguments), cancellationToken, true);
    public Task<string> CallStringAsync(string module, uint ordinal, object[] arguments, RpcThread thread = RpcThread.System, CancellationToken cancellationToken = default) => SendAsync(RpcWire.BuildCall(0, module, ordinal, 2, IsSystem(thread), 0, arguments), cancellationToken, true);
    public async Task<uint> ResolveFunctionAsync(string module, uint ordinal, CancellationToken cancellationToken = default) => Hex32(await FeatureAsync(9, 0, [module, ordinal], cancellationToken).ConfigureAwait(false));
    public Task<string> GetCpuKeyAsync(CancellationToken cancellationToken = default) => FeatureAsync(10, 0, [], cancellationToken);
    public async Task ShutdownAsync(CancellationToken cancellationToken = default) => _ = await FeatureAsync(11, 0, [], cancellationToken).ConfigureAwait(false);
    public async Task NotifyAsync(string text, uint type = 34, CancellationToken cancellationToken = default) => _ = await FeatureAsync(12, 0, [text, type], cancellationToken).ConfigureAwait(false);
    public async Task<uint> GetKernelVersionAsync(CancellationToken cancellationToken = default) => uint.Parse(await FeatureAsync(13, 0, [], cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    public async Task SetLedsAsync(LedState topLeft, LedState topRight, LedState bottomLeft, LedState bottomRight, CancellationToken cancellationToken = default)
    {
        if (new[] { topLeft, topRight, bottomLeft, bottomRight }.Any(v => !Enum.IsDefined(v))) throw new ArgumentOutOfRangeException(nameof(topLeft));
        _ = await FeatureAsync(14, 0, [(uint)topLeft, (uint)topRight, (uint)bottomLeft, (uint)bottomRight], cancellationToken).ConfigureAwait(false);
    }
    public async Task<uint> GetTemperatureAsync(TemperatureSensor sensor, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(sensor)) throw new ArgumentOutOfRangeException(nameof(sensor));
        return Hex32(await FeatureAsync(15, 0, [(int)sensor], cancellationToken).ConfigureAwait(false));
    }
    public async Task<uint> GetCurrentTitleIdAsync(CancellationToken cancellationToken = default) => Hex32(await FeatureAsync(16, 0, [], cancellationToken).ConfigureAwait(false));
    public Task<string> GetConsoleTypeAsync(CancellationToken cancellationToken = default) => FeatureAsync(17, 0, [], cancellationToken);
    public async Task SetContinuousMemoryAsync(uint address, uint value, uint? ifValue = null, uint? titleId = null, CancellationToken cancellationToken = default) => _ = await FeatureAsync(18, address, [value, ifValue.HasValue ? 1 : 0, ifValue ?? 0, titleId.HasValue ? 1 : 0, titleId ?? 0], cancellationToken).ConfigureAwait(false);
    private Task<string> FeatureAsync(int type, uint address, object[] arguments, CancellationToken ct) => SendAsync(RpcWire.BuildFeature(type, address, arguments), ct);
    private async Task<string> SendAsync(string command, CancellationToken ct, bool arbitraryText = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CallTimeout);
        await calls.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var response = await connection.ExecuteAsync(command, deadline.Token).ConfigureAwait(false);
                if (response.StatusCode != 200) throw new InvalidDataException("Unexpected JRPC response status.");
                if (!arbitraryText && response.Message.StartsWith("error=", StringComparison.OrdinalIgnoreCase)) throw new IOException("JRPC: " + response.Message);
                if (!arbitraryText && response.Message.Equals("DEBUG", StringComparison.Ordinal)) throw new IOException("The console does not have the JRPC consolefeatures handler.");
                var fields = response.Message.StartsWith("buf_addr=", StringComparison.OrdinalIgnoreCase) ? ProtocolFields.Parse(response.Message) : null;
                if (fields is null || !fields.TryGetValue("buf_addr", out var address)) return response.Message;
                var buffer = Hex32(address);
                await Task.Delay(250, deadline.Token).ConfigureAwait(false);
                command = "consolefeatures buf_addr=0x" + buffer.ToString("X", CultureInfo.InvariantCulture);
            }
        }
        finally { calls.Release(); }
    }
    private static bool IsSystem(RpcThread thread) => Enum.IsDefined(thread) ? thread == RpcThread.System : throw new ArgumentOutOfRangeException(nameof(thread));
    private static int ReturnType<T>(bool array) where T : struct
    {
        var type = typeof(T);
        if (type == typeof(float) || type == typeof(double)) return array ? 6 : 3;
        if (type == typeof(byte) || type == typeof(sbyte)) return array ? 7 : 4;
        if (type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)) return array ? 5 : 1;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(bool)) return array ? 9 : 8;
        throw new ArgumentException("Unsupported RPC return type " + type.Name);
    }
    private static uint Hex32(string text) => uint.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.AsSpan(2) : text.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static T ParseScalar<T>(string text, bool decimalByte = false) where T : struct
    {
        object value; var type = typeof(T);
        if (type == typeof(float)) value = float.Parse(text, CultureInfo.InvariantCulture);
        else if (type == typeof(double)) value = double.Parse(text, CultureInfo.InvariantCulture);
        else
        {
            var bits = ulong.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.AsSpan(2) : text.AsSpan(), decimalByte ? NumberStyles.Integer : NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (type == typeof(long)) value = unchecked((long)bits);
            else if (type == typeof(ulong)) value = bits;
            else if (type == typeof(uint)) value = checked((uint)bits);
            else if (type == typeof(int)) value = unchecked((int)checked((uint)bits));
            else if (type == typeof(short)) value = unchecked((short)bits);
            else if (type == typeof(ushort)) value = unchecked((ushort)bits);
            else if (type == typeof(byte)) value = checked((byte)bits);
            else if (type == typeof(sbyte)) value = unchecked((sbyte)checked((byte)bits));
            else if (type == typeof(bool)) value = bits != 0;
            else throw new ArgumentException("Unsupported RPC return type " + type.Name);
        }
        return (T)value;
    }
    private static T[] ParseArray<T>(string response, int count) where T : struct
    {
        var parts = response.Trim().TrimEnd(';').Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != count) throw new InvalidDataException("RPC array length differs from the requested length.");
        return parts.Select(p => ParseScalar<T>(p, typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte))).ToArray();
    }
}
