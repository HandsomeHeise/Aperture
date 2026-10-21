using System.Buffers.Binary;
using Aperture.Debugging;
using Aperture.Models;
using Xunit;

namespace Aperture.Tests;
public sealed class FunctionTableTests
{
    [Fact]
    public void PdataUsesXenonBitOrderAndInstructionSizedLengths()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(data, 0x82001003);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (3u << 30) | (64u << 8) | 6u);
        var table = new FunctionTable(data);
        var function = table.Find(0x82001080)!;
        Assert.Equal(FunctionType.Handler, function.FunctionType);
        Assert.Equal(0x82001000u, function.BeginAddress);
        Assert.Equal(0x82001018u, function.PrologEndAddress);
        Assert.Equal(0x82001100u, function.FunctionEndAddress);
        Assert.Null(table.Find(0x82001100));
    }
}
