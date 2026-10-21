using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Aperture.Memory;
using Aperture.Transport;

namespace Aperture.Rpc;

public static class RpcWire
{
    public static string BuildCall(uint address, string? module, uint ordinal, int returnType, bool systemThread, uint arraySize, object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length > 37) throw new ArgumentException("JRPC accepts at most 37 arguments.", nameof(arguments));
        var command = new StringBuilder(FormattableString.Invariant($"consolefeatures ver=2 type={returnType}{(systemThread ? " system" : "")}"));
        if (module is not null) command.Append(FormattableString.Invariant($" module={ProtocolFields.Quote(module)} ord={ordinal}"));
        command.Append(FormattableString.Invariant($" as={arraySize} params=\"A\\{address:X}\\A\\{arguments.Length}\\"));
        foreach (var argument in arguments) AppendArgument(command, argument);
        return command.Append('"').ToString();
    }
    public static string BuildFeature(int type, uint address, params object[] arguments)
    {
        if (arguments.Length > 37) throw new ArgumentException("JRPC accepts at most 37 arguments.", nameof(arguments));
        var command = new StringBuilder(FormattableString.Invariant($"consolefeatures ver=2 type={type} params=\"A\\{address:X}\\A\\{arguments.Length}\\"));
        foreach (var argument in arguments) AppendArgument(command, argument, 2);
        return command.Append('"').ToString();
    }
    private static void AppendArgument(StringBuilder command, object argument, int stringType = 7)
    {
        switch (argument)
        {
            case bool value: command.Append("1/").Append(value ? '1' : '0').Append('\\'); return;
            case uint value: Scalar(1, unchecked((int)value)); return;
            case int value: Scalar(1, value); return;
            case byte value: Scalar(1, value); return;
            case float value: Scalar(3, value); return;
            case double value: Scalar(3, value); return;
            case string value:
                if (value.Any(c => c > 255)) throw new ArgumentException("JRPC string arguments use single-byte characters; use an explicitly encoded byte array for Unicode.");
                var textBytes = Encoding.Latin1.GetBytes(value);
                command.Append(stringType).Append('/').Append(textBytes.Length.ToString(CultureInfo.InvariantCulture)).Append('\\').Append(Convert.ToHexString(textBytes)).Append('\\'); return;
            case byte[] value: Bytes(value); return;
            case int[] value:
                var integers = new byte[checked(value.Length * 4)];
                for (var i = 0; i < value.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(integers.AsSpan(i * 4), value[i]);
                Bytes(integers); return;
            case uint[] value:
                var unsigned = new byte[checked(value.Length * 4)];
                for (var i = 0; i < value.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(unsigned.AsSpan(i * 4), value[i]);
                Bytes(unsigned); return;
            case float[] value: Bytes(BigEndianCodec.EncodeArray<float>(value)); return;
            case double[] value: Bytes(BigEndianCodec.EncodeArray<double>(value)); return;
            case long[] value: Bytes(BigEndianCodec.EncodeArray<long>(value)); return;
            case ulong[] value: Bytes(BigEndianCodec.EncodeArray<ulong>(value)); return;
            case short[] value: Bytes(BigEndianCodec.EncodeArray<short>(value)); return;
            case ushort[] value: Bytes(BigEndianCodec.EncodeArray<ushort>(value)); return;
            case bool[] value: Bytes(BigEndianCodec.EncodeArray<bool>(value)); return;
            case sbyte[] value: Bytes(BigEndianCodec.EncodeArray<sbyte>(value)); return;
            case short value: Scalar(8, unchecked((ulong)(long)value)); return;
            case ushort value: Scalar(8, value); return;
            case sbyte value: Scalar(8, unchecked((ulong)(long)value)); return;
            case long value: Scalar(8, unchecked((ulong)value)); return;
            case ulong value: Scalar(8, value); return;
            default: throw new ArgumentException("Unsupported RPC argument type " + (argument?.GetType().Name ?? "null"));
        }
        void Scalar(int type, object value) => command.Append(type).Append('\\').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\\');
        void Bytes(byte[] bytes) => command.Append("7/").Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append('\\').Append(Convert.ToHexString(bytes)).Append('\\');
    }
}
