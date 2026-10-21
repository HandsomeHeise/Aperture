using System.Buffers.Binary;
using System.Globalization;
using Aperture.Debugging;
using Aperture.Models;

namespace Aperture.Dumps;

internal static class XenonContextCodec
{
    public static byte[] Encode(RegisterSet registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        CompleteGroup("control", [registers.Has32(Register32.msr), registers.Has32(Register32.iar), registers.Has32(Register32.lr), registers.Has64(Register64.ctr)]);
        CompleteGroup("integer", [.. Enumerable.Range(0, 32).Select(i => registers.Has64(Enum.Parse<Register64>("r" + i))), registers.Has32(Register32.cr), registers.Has32(Register32.xer)]);
        CompleteGroup("floating point", [.. Enumerable.Range(0, 32).Select(i => registers.HasDouble(Enum.Parse<RegisterDouble>("fp" + i))), registers.HasDouble(RegisterDouble.fpscr)]);
        CompleteGroup("vector", [.. Enumerable.Range(0, 128).Select(i => registers.HasVector(Enum.Parse<RegisterVector>("v" + i))), registers.HasVector(RegisterVector.vscr)]);
        var data = new byte[2616]; uint flags = 0;
        if (registers.Has32(Register32.msr) && registers.Has32(Register32.iar) && registers.Has32(Register32.lr) && registers.Has64(Register64.ctr))
        {
            flags |= 1; U32(4, registers.Get32(Register32.msr)); U32(8, registers.Get32(Register32.iar)); U32(12, registers.Get32(Register32.lr)); U64(16, registers.Get64(Register64.ctr));
        }
        if (Enumerable.Range(0, 32).All(i => registers.Has64(Enum.Parse<Register64>("r" + i))) && registers.Has32(Register32.cr) && registers.Has32(Register32.xer))
        {
            flags |= 4;
            for (var i = 0; i < 32; i++) U64(24 + i * 8, registers.Get64(Enum.Parse<Register64>("r" + i)));
            U32(280, registers.Get32(Register32.cr)); U32(284, registers.Get32(Register32.xer));
        }
        if (Enumerable.Range(0, 32).All(i => registers.HasDouble(Enum.Parse<RegisterDouble>("fp" + i))) && registers.HasDouble(RegisterDouble.fpscr))
        {
            flags |= 2; U64(288, unchecked((ulong)BitConverter.DoubleToInt64Bits(registers.GetDouble(RegisterDouble.fpscr))));
            for (var i = 0; i < 32; i++) U64(296 + i * 8, unchecked((ulong)BitConverter.DoubleToInt64Bits(registers.GetDouble(Enum.Parse<RegisterDouble>("fp" + i)))));
        }
        if (Enumerable.Range(0, 128).All(i => registers.HasVector(Enum.Parse<RegisterVector>("v" + i))) && registers.HasVector(RegisterVector.vscr))
        {
            flags |= 16; Vector(552, registers.GetVector(RegisterVector.vscr));
            for (var i = 0; i < 128; i++) Vector(568 + i * 16, registers.GetVector(Enum.Parse<RegisterVector>("v" + i)));
        }
        if (flags == 0) throw new InvalidDataException("Context has no complete register group.");
        U32(0, flags);
        var length = (flags & 16) != 0 ? 2616 : (flags & 2) != 0 ? 552 : (flags & 4) != 0 ? 288 : 24;
        return data[..length];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
        static void CompleteGroup(string name, bool[] present)
        {
            if (present.Any(value => value) && !present.All(value => value)) throw new InvalidDataException("Incomplete " + name + " register group cannot be stored without data loss.");
        }
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
        void Vector(int offset, System.Numerics.Vector4 value)
        {
            U32(offset, unchecked((uint)BitConverter.SingleToInt32Bits(value.X))); U32(offset + 4, unchecked((uint)BitConverter.SingleToInt32Bits(value.Y))); U32(offset + 8, unchecked((uint)BitConverter.SingleToInt32Bits(value.Z))); U32(offset + 12, unchecked((uint)BitConverter.SingleToInt32Bits(value.W)));
        }
    }
    public static RegisterSet Decode(byte[] data)
    {
        if (data.Length < 4) throw new InvalidDataException("Truncated Xenon context.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data); var bigEndian = false;
        if ((flags & ~0x17u) != 0)
        {
            flags = BinaryPrimitives.ReadUInt32BigEndian(data); bigEndian = true;
            if ((flags & ~0x17u) != 0) throw new InvalidDataException("Unknown Xenon context flags.");
        }
        var lines = new List<string>();
        if ((flags & 1) != 0)
        {
            Require(24);
            Add32("Msr", 4); Add32("Iar", 8); Add32("Lr", 12); Add64("Ctr", 16);
        }
        if ((flags & 4) != 0)
        {
            Require(288);
            for (var i = 0; i < 32; i++) Add64("Gpr" + i, 24 + i * 8);
            Add32("Cr", 280); Add32("Xer", 284);
        }
        if ((flags & 2) != 0)
        {
            Require(552); Add64("Fpscr", 288);
            for (var i = 0; i < 32; i++) Add64("Fpr" + i, 296 + i * 8);
        }
        if ((flags & 16) != 0)
        {
            Require(2616); AddVector("Vscr", 552);
            for (var i = 0; i < 128; i++) AddVector("Vr" + i, 568 + i * 16);
        }
        return RegisterSet.Parse(lines.ToArray());

        void Require(int count) { if (data.Length < count) throw new InvalidDataException("Truncated Xenon context register group."); }
        uint U32(int offset) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)) : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        ulong U64(int offset) => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset, 8)) : BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
        void Add32(string name, int offset) => lines.Add(name + "=0x" + U32(offset).ToString("X8", CultureInfo.InvariantCulture));
        void Add64(string name, int offset) => lines.Add(name + "=0q" + U64(offset).ToString("X16", CultureInfo.InvariantCulture));
        void AddVector(string name, int offset) => lines.Add(name + "=" + string.Join(',', Enumerable.Range(0, 4).Select(i => "0x" + U32(offset + i * 4).ToString("X8", CultureInfo.InvariantCulture))));
    }
}
