using System.Buffers.Binary;
using Aperture.Debugging;
using Aperture.Models;
using Xunit;

namespace Aperture.Tests;
public sealed class UnwindTests
{
    [Fact]
    public async Task MissingFunctionMetadataDoesNotInventLeafFrame()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q0000000000010000 Iar=0x82001040 Lr=0x82001030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => throw new InvalidOperationException("No metadata permits a memory read.");
        await Assert.ThrowsAsync<NotSupportedException>(() => PowerPcUnwinder.UnwindAsync(registers, null, Read));
    }
    [Fact]
    public async Task LinkRegisterProvenanceSurvivesRegisterMove()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q0000000000010000 Iar=0x82001010 Lr=0x82001080"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct)
        {
            var bytes = new byte[length];
            if (address == 0x10078) BinaryPrimitives.WriteUInt64BigEndian(bytes, 0x82002030);
            else
            {
                var code = new uint[] { 0x7C0802A6, 0x7C0C0378, 0xF981FFF8, 0x9421FF80 };
                for (var i = 0; i < code.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), code[i]);
            }
            return Task.FromResult(bytes);
        }
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001010, 0x82001100), Read);
        Assert.Equal(0x82002030u, caller!.Get32(Register32.iar));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedStackAllocationAndExecutedEpilogFailExplicitly(bool epilog)
    {
        var registers = RegisterSet.Parse(["Gpr1=0q0000000000010000 Iar=0x82001008 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i += 4)
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i), epilog ? (address == 0x82001000 ? 0x9421FF80u : 0x38210080u) : 0x7C2C0850u);
            return Task.FromResult(bytes);
        }
        await Assert.ThrowsAsync<NotSupportedException>(() => PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, epilog ? 0x82001004u : 0x82001008u, 0x82001100), Read));
    }
    [Fact]
    public async Task UnknownPrologBranchesFailRatherThanReturnInventedFrames()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q0000000000010000 Iar=0x82001004 Lr=0x82009900"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(new byte[] { 0x48, 0, 0, 1 });
        await Assert.ThrowsAsync<NotSupportedException>(() => PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001100), Read));
    }
    [Fact]
    public async Task RestoresCallerStackLinkAndNonvolatileRegisterFromExecutedProlog()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q0000000000010000 Iar=0x82001020 Lr=0x82009900 Gpr14=0q0000000000001111"]);
        var prolog = new byte[16];
        var instructions = new uint[] { 0x7C0802A6, 0xF9C1FFF0, 0xF801FFF8, 0x9421FF80 };
        for (var i = 0; i < instructions.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(prolog.AsSpan(i * 4), instructions[i]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct)
        {
            if (address == 0x82001000) return Task.FromResult(prolog);
            if (address >= 0x82001010 && address < 0x82001100)
            {
                var code = new byte[length];
                for (var i = 0; i < length; i += 4) BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(i), 0x60000000);
                return Task.FromResult(code);
            }
            var result = new byte[length];
            BinaryPrimitives.WriteUInt64BigEndian(result, address == 0x10070 ? 0x2222UL : address == 0x10078 ? 0x82002030UL : throw new InvalidOperationException("Unexpected read."));
            return Task.FromResult(result);
        }
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001010, 0x82001100), Read);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
        Assert.Equal(0x82002030u, caller.Get32(Register32.iar));
        Assert.Equal(0x2222UL, caller.Get64(Register64.r14));
        Assert.False(caller.Dirty);
        Assert.Equal(0x82002030UL, caller.Get64(Register64.r0));
    }
}

