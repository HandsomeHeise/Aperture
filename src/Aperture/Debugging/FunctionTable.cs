using System.Buffers.Binary;
using Aperture.Models;

namespace Aperture.Debugging;

public sealed record FunctionInfo(FunctionType FunctionType, uint BeginAddress, uint PrologEndAddress, uint FunctionEndAddress);
public sealed class FunctionTable
{
    private readonly FunctionInfo[] functions;
    public IReadOnlyList<FunctionInfo> Functions => Array.AsReadOnly(functions);
    public FunctionTable(ReadOnlySpan<byte> pdata)
    {
        if ((pdata.Length & 7) != 0) throw new InvalidDataException("Truncated Xenon runtime function table.");
        functions = new FunctionInfo[pdata.Length / 8];
        for (var i = 0; i < functions.Length; i++)
        {
            var begin = BinaryPrimitives.ReadUInt32BigEndian(pdata.Slice(i * 8, 4)) & ~3u;
            var packed = BinaryPrimitives.ReadUInt32BigEndian(pdata.Slice(i * 8 + 4, 4));
            var length = ((packed >> 8) & 0x3fffff) * 4;
            var prolog = (packed & 255) * 4;
            if (length == 0 || prolog > length || (ulong)begin + length > uint.MaxValue || i > 0 && begin < functions[i - 1].FunctionEndAddress) throw new InvalidDataException("Invalid or overlapping runtime function entry.");
            functions[i] = new((FunctionType)(packed >> 30), begin, begin + prolog, begin + length);
        }
    }
    public FunctionInfo? Find(uint address)
    {
        var low = 0; var high = functions.Length - 1;
        while (low <= high)
        {
            var index = low + (high - low) / 2; var function = functions[index];
            if (address < function.BeginAddress) high = index - 1;
            else if (address >= function.FunctionEndAddress) low = index + 1;
            else return function;
        }
        return null;
    }
}
