using System.Buffers.Binary;

namespace Aperture.Memory;

public static class BigEndianCodec
{
    public static int SizeOf<T>() where T : struct => typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(bool) ? 1 : typeof(T) == typeof(short) || typeof(T) == typeof(ushort) ? 2 : typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(float) ? 4 : typeof(T) == typeof(long) || typeof(T) == typeof(ulong) || typeof(T) == typeof(double) ? 8 : throw new ArgumentException("Unsupported primitive type " + typeof(T).Name);
    public static byte[] Encode<T>(T value) where T : struct
    {
        var data = new byte[SizeOf<T>()];
        switch (value)
        {
            case byte v: data[0] = v; break;
            case sbyte v: data[0] = unchecked((byte)v); break;
            case bool v: data[0] = v ? (byte)1 : (byte)0; break;
            case short v: BinaryPrimitives.WriteInt16BigEndian(data, v); break;
            case ushort v: BinaryPrimitives.WriteUInt16BigEndian(data, v); break;
            case int v: BinaryPrimitives.WriteInt32BigEndian(data, v); break;
            case uint v: BinaryPrimitives.WriteUInt32BigEndian(data, v); break;
            case long v: BinaryPrimitives.WriteInt64BigEndian(data, v); break;
            case ulong v: BinaryPrimitives.WriteUInt64BigEndian(data, v); break;
            case float v: BinaryPrimitives.WriteSingleBigEndian(data, v); break;
            case double v: BinaryPrimitives.WriteDoubleBigEndian(data, v); break;
        }
        return data;
    }
    public static T Decode<T>(byte[] data) where T : struct => DecodeSpan<T>(data);
    internal static T DecodeSpan<T>(ReadOnlySpan<byte> data) where T : struct
    {
        if (data.Length != SizeOf<T>()) throw new ArgumentException("Primitive payload has the wrong length.", nameof(data));
        object value;
        if (typeof(T) == typeof(byte)) value = data[0];
        else if (typeof(T) == typeof(sbyte)) value = unchecked((sbyte)data[0]);
        else if (typeof(T) == typeof(bool)) value = data[0] != 0;
        else if (typeof(T) == typeof(short)) value = BinaryPrimitives.ReadInt16BigEndian(data);
        else if (typeof(T) == typeof(ushort)) value = BinaryPrimitives.ReadUInt16BigEndian(data);
        else if (typeof(T) == typeof(int)) value = BinaryPrimitives.ReadInt32BigEndian(data);
        else if (typeof(T) == typeof(uint)) value = BinaryPrimitives.ReadUInt32BigEndian(data);
        else if (typeof(T) == typeof(long)) value = BinaryPrimitives.ReadInt64BigEndian(data);
        else if (typeof(T) == typeof(ulong)) value = BinaryPrimitives.ReadUInt64BigEndian(data);
        else if (typeof(T) == typeof(float)) value = BinaryPrimitives.ReadSingleBigEndian(data);
        else value = BinaryPrimitives.ReadDoubleBigEndian(data);
        return (T)value;
    }
    public static byte[] EncodeArray<T>(ReadOnlySpan<T> values) where T : struct
    {
        var size = SizeOf<T>(); var result = new byte[checked(values.Length * size)];
        for (var i = 0; i < values.Length; i++) Encode(values[i]).CopyTo(result, i * size);
        return result;
    }
    public static T[] DecodeArray<T>(ReadOnlySpan<byte> data) where T : struct
    {
        var size = SizeOf<T>();
        if (data.Length % size != 0) throw new ArgumentException("Array payload is not aligned.", nameof(data));
        var values = new T[data.Length / size];
        for (var i = 0; i < values.Length; i++) values[i] = DecodeSpan<T>(data.Slice(i * size, size));
        return values;
    }
}
