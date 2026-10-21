using System.Buffers.Binary;
using System.Numerics;
using Aperture.Models;

namespace Aperture.Debugging;

internal sealed record EpilogResult(RegisterSet? Caller);

internal static class PowerPcEpilog
{
    internal static async Task<EpilogResult?> TryUnwindAsync(RegisterSet current, FunctionInfo function, Func<uint, int, CancellationToken, Task<byte[]>> read, CancellationToken ct, Func<uint, CancellationToken, Task<FunctionInfo?>>? lookup)
    {
        var pc = current.Get32(Register32.iar);
        var words = await CollectAsync(pc, function, read, ct, lookup, new()).ConfigureAwait(false);
        if (words is null) return null;
        var result = current.Clone();
        foreach (var word in words)
        {
            var op = word >> 26; var rt = (int)(word >> 21 & 31); var ra = (int)(word >> 16 & 31);
            if (PowerPcVmx128.TryDecode(word, out var vector, out var vectorRa, out var vectorRb, out var vectorLoad) && vectorLoad)
            {
                var address = unchecked((uint)((vectorRa == 0 ? 0 : result.Get64(Enum.Parse<Register64>("r" + vectorRa))) + result.Get64(Enum.Parse<Register64>("r" + vectorRb)))) & ~15u;
                var bytes = await read(address, 16, ct).ConfigureAwait(false);
                if (bytes.Length != 16) throw new EndOfStreamException("Truncated saved epilog vector.");
                float F(int offset) => BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))));
                result.SetVector(Enum.Parse<RegisterVector>("v" + vector), new Vector4(F(0), F(4), F(8), F(12)));
            }
            else if (op == 14) result.Set64(Register64.r1, checked((ulong)((long)result.Get64(Register64.r1) + unchecked((short)word))));
            else if ((word & 0xfc1fffff) == 0x7c0803a6) result.Set32(Register32.lr, checked((uint)result.Get64(Enum.Parse<Register64>("r" + rt))));
            else
            {
                var size = op == 32 ? 4 : 8;
                var displacement = (short)(word & (op == 58 ? 0xfffcu : 0xffffu));
                var baseRegister = Enum.Parse<Register64>("r" + ra);
                var address = checked((uint)((ra == 0 ? 0 : (long)result.Get64(baseRegister)) + displacement));
                var saved = await read(address, size, ct).ConfigureAwait(false);
                if (saved.Length != size) throw new EndOfStreamException("Truncated saved epilog register.");
                var value = size == 4 ? BinaryPrimitives.ReadUInt32BigEndian(saved) : BinaryPrimitives.ReadUInt64BigEndian(saved);
                if (op == 50) result.SetDouble(Enum.Parse<RegisterDouble>("fp" + rt), BitConverter.Int64BitsToDouble(unchecked((long)value)));
                else result.Set64(Enum.Parse<Register64>("r" + rt), value);
            }
        }
        var caller = result.Get32(Register32.lr);
        if (caller == 0) return new(null);
        if ((caller & 3) != 0 || caller == pc || (result.Get64(Register64.r1) & 15) != 0) throw new InvalidDataException("Invalid epilog caller context.");
        result.Set32(Register32.iar, caller);
        return new(result.Clone());
    }
    private static async Task<List<uint>?> CollectAsync(uint pc, FunctionInfo function, Func<uint, int, CancellationToken, Task<byte[]>> read, CancellationToken ct, Func<uint, CancellationToken, Task<FunctionInfo?>>? lookup, HashSet<uint> visited)
    {
        if (!visited.Add(pc) || visited.Count > 8) throw new InvalidDataException("Cyclic or excessive restore millicode.");
        if (pc < function.BeginAddress || pc >= function.FunctionEndAddress) return null;
        var end = function.FunctionType == FunctionType.RestoreMillicode ? function.PrologEndAddress : function.FunctionEndAddress;
        if (end <= pc) return null;
        var length = (int)Math.Min(4096u, end - pc);
        if ((length & 3) != 0) return null;
        var words = new List<uint>();
        for (var i = 0; i < length; i += 4)
        {
            var bytes = await read(checked(pc + (uint)i), 4, ct).ConfigureAwait(false);
            if (bytes.Length != 4) throw new EndOfStreamException("Truncated epilog instruction.");
            var word = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            if (word == 0x4e800020) return words;
            var op = word >> 26; var ra = (word >> 16) & 31;
            if (op == 18 && lookup is not null)
            {
                var displacement = unchecked((int)(word & 0x03fffffc));
                if ((displacement & 0x02000000) != 0) displacement |= unchecked((int)0xfc000000);
                var target = (word & 2) != 0 ? unchecked((uint)displacement) : checked((uint)((long)pc + i + displacement));
                var helper = await lookup(target, ct).ConfigureAwait(false);
                if (helper?.FunctionType != FunctionType.RestoreMillicode) return null;
                var restored = await CollectAsync(target, helper, read, ct, lookup, visited).ConfigureAwait(false);
                if (restored is null) return null;
                words.AddRange(restored);
                if ((word & 1) == 0) return words;
                continue;
            }
            var load = (op is 32 or 50 || op == 58 && (word & 3) == 0) && (ra == 1 || function.FunctionType == FunctionType.RestoreMillicode);
            var stack = op == 14 && ra == 1 && (word >> 21 & 31) == 1;
            var link = (word & 0xfc1fffff) == 0x7c0803a6;
            var vectorLoad = PowerPcVmx128.TryDecode(word, out _, out _, out _, out var isLoad) && isLoad;
            if (!(load || stack || link || vectorLoad)) return null;
            words.Add(word);
        }
        return function.FunctionType == FunctionType.RestoreMillicode && pc + length == function.PrologEndAddress ? words : null;
    }

}

