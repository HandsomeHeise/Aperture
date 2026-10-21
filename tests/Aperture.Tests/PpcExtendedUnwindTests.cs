using System.Buffers.Binary;
using System.Numerics;
using Aperture.Debugging;
using Aperture.Models;
using Xunit;

namespace Aperture.Tests;

public sealed class PpcExtendedUnwindTests
{
    [Fact]
    public async Task RestoresHighVmx128RegisterSavedBeforeFrameAllocation()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x8200100C Lr=0x82002030"]);
        registers.SetVector(RegisterVector.v64, Vector4.Zero);
        var saved = VectorBytes(new(1.25f, -2.5f, 3.75f, -4.5f));
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001000 ? Words(0x3980FFF0, 0x100161CB, 0x9421FF80) : address == 0x10070 ? saved : throw new InvalidOperationException());
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x8200100C, 0x82001100), Read);
        Assert.Equal(new Vector4(1.25f, -2.5f, 3.75f, -4.5f), caller!.GetVector(RegisterVector.v64));
        Assert.Equal(0x10080UL, caller.Get64(Register64.r1));
    }
    [Theory]
    [InlineData("Gpr1=0q10000 Gpr12=0q70", 0x38210080u)]
    [InlineData("Gpr1=0q10080 Gpr12=0qFFFFFFFFFFFFFFF0", 0x38210000u)]
    public async Task RemainingEpilogLoadsHighVmx128RegisterBeforeReturning(string context, uint stackInstruction)
    {
        var registers = RegisterSet.Parse([context + " Iar=0x82001020 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address switch {
            0x82001020 => Words(0x100160CB), 0x82001024 => Words(stackInstruction), 0x82001028 => Words(0x4E800020), 0x10070 => VectorBytes(new(2, 4, 6, 8)), _ => throw new InvalidOperationException() });
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x8200102C), Read);
        Assert.Equal(new Vector4(2, 4, 6, 8), caller!.GetVector(RegisterVector.v64));
        Assert.Equal(0x10080UL, caller.Get64(Register64.r1));
    }
    [Fact]
    public async Task VerifiedRestoreMillicodeLoadsFromItsActualBaseRegister()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Gpr11=0q10070 Gpr0=0q82002030 Iar=0x82001000 Lr=0x82001040"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address switch { 0x82001000 => Words(0x81CB0000), 0x82001004 => Words(0x7C0803A6), 0x10070 => Words(0x1234), _ => throw new InvalidOperationException() });
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.RestoreMillicode, 0x82001000, 0x82001008, 0x82001008), Read);
        Assert.Equal(0x1234UL, caller!.Get64(Register64.r14));
        Assert.Equal(0x82002030u, caller.Get32(Register32.iar));
    }
    [Fact]
    public async Task LargeIndexedFramesAcceptLisAndRecoverBackchain()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001008 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001000 ? Words(0x3D80FFFF, 0x7C21616E) : Words(0x20000));
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001008, 0x82001100), Read);
        Assert.Equal(0x20000UL, caller!.Get64(Register64.r1));
    }
    [Fact]
    public async Task TruncatedSaveMillicodeCannotBeAcceptedAsACompleteSaveSequence()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001004 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001000 ? Words(0x48001001) : Words(0x9421FF80));
        Task<FunctionInfo?> Lookup(uint address, CancellationToken ct) => Task.FromResult<FunctionInfo?>(new(FunctionType.SaveMillicode, 0x82002000, 0x82002008, 0x82002008));
        await Assert.ThrowsAsync<EndOfStreamException>(() => PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001100), Read, lookupFunction: Lookup));
    }
    [Fact]
    public async Task LogicalStackRestorationCannotBeMistakenForAnUnexecutedEpilog()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10080 Iar=0x8200100C Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address switch { 0x82001000 => Words(0x9421FF80), 0x82001004 => Words(0x39810080, 0x61810000), _ => Words(0x60000000) });
        await Assert.ThrowsAsync<NotSupportedException>(() => PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001010), Read));
    }
    [Fact]
    public async Task RestoresSavedVolatileRegisterAndArgumentHomeFromBody()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x8200100C Lr=0x82002030 Gpr3=0q99"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct)
        {
            if (address == 0x82001000) return Task.FromResult(Words(0xF961FFF8, 0x9421FF80));
            if (address == 0x82001008) return Task.FromResult(Words(0xF8610020));
            if (address == 0x8200100C) return Task.FromResult(Words(0x60000000));
            var data = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(data, address == 0x10078 ? 0x1234UL : address == 0x10020 ? 0x5678UL : throw new InvalidOperationException()); return Task.FromResult(data);
        }
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001008, 0x82001010), Read);
        Assert.Equal(0x1234UL, caller!.Get64(Register64.r11));
        Assert.Equal(0x5678UL, caller.Get64(Register64.r3));
        Assert.Equal(0x82002030u, caller.Get32(Register32.lr));
    }
    [Fact]
    public async Task SaveMillicodeUsesMetadataExtentWithoutInventingReturnInstruction()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001004 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address switch { 0x82001000 => Words(0x48001001), 0x82002000 => Words(0x9421FF80), _ => throw new InvalidOperationException() });
        Task<FunctionInfo?> Lookup(uint address, CancellationToken ct) => Task.FromResult<FunctionInfo?>(new(FunctionType.SaveMillicode, 0x82002000, 0x82002004, 0x82002004));
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001100), Read, lookupFunction: Lookup);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
    }
    [Fact]
    public async Task ExplicitVerifiedNoPDataLeafUsesLiveLinkWithoutReadingMemory()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001000 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => throw new InvalidOperationException();
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoPData, 0x82001000, 0x82001000, 0x82001100), Read);
        Assert.Equal(0x10000UL, caller!.Get64(Register64.r1));
        Assert.Equal(0x82002030u, caller.Get32(Register32.iar));
    }
    [Fact]
    public async Task MixedImmediateAndIndexedFramesRecoverOriginalCaller()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001008 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001000 ? Words(0x9421FF80, 0x7C21616E) : address == 0x10000 ? Words(0x10080) : throw new InvalidOperationException());
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001008, 0x82001100), Read);
        Assert.Equal(0x10100UL, caller!.Get64(Register64.r1));
    }
    [Fact]
    public async Task FollowsVerifiedRestoreMillicodeTailBranch()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001020 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address switch {
            0x82001020 => Words(0x48000FE0),
            0x82002000 => Words(0x38210080),
            0x82002004 => Words(0x4E800020),
            _ => throw new InvalidOperationException("Only remaining restore instructions should be read.") });
        Task<FunctionInfo?> Lookup(uint address, CancellationToken ct) => Task.FromResult<FunctionInfo?>(address == 0x82002000 ? new(FunctionType.RestoreMillicode, 0x82002000, 0x82002008, 0x82002008) : null);
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001024), Read, lookupFunction: Lookup);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
    }
    [Fact]
    public async Task FinalEpilogWithZeroLinkTerminatesStack()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10080 Iar=0x82001024 Lr=0x0"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001024 ? Words(0x4E800020) : throw new InvalidOperationException("Terminal epilog must not read prior prolog."));
        Assert.Null(await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001028), Read));
    }
    [Fact]
    public async Task ConstantArithmeticUpdatesAllocationBeforeStackRecovery()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x8200100C Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(Words(0x39800080, 0x398C0080, 0x7C2C0850));
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x8200100C, 0x82001100), Read);
        Assert.Equal(0x10100UL, caller!.Get64(Register64.r1));
    }
    [Fact]
    public async Task IndexedAllocationRecoversCallerFromBackchain()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001004 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001000 ? Words(0x7C21616E) : address == 0x10000 ? Words(0x10080) : throw new InvalidOperationException());
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001100), Read);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
    }
    [Fact]
    public async Task UnwindsConstantRegisterStackAllocation()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001008 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(Words(0x39800080, 0x7C2C0850));
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001008, 0x82001100), Read);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
    }

    [Fact]
    public async Task FinishesRemainingEpilogWithoutUndoingAlreadyRestoredStack()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10080 Iar=0x82001024 Lr=0x82002030"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct) => Task.FromResult(address == 0x82001024 ? Words(0x4E800020) : throw new InvalidOperationException("Only remaining BLR should be read."));
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001004, 0x82001028), Read);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
        Assert.Equal(0x82002030u, caller.Get32(Register32.iar));
    }

    [Fact]
    public async Task EpilogLoadsSavedLinkRegisterAndRestoresStack()
    {
        var registers = RegisterSet.Parse(["Gpr1=0q10000 Iar=0x82001020 Lr=0x82001080"]);
        Task<byte[]> Read(uint address, int length, CancellationToken ct)
        {
            if (address >= 0x82001020 && address < 0x82001030) return Task.FromResult(Words(0xE8010078, 0x7C0803A6, 0x38210080, 0x4E800020).AsSpan((int)(address - 0x82001020), length).ToArray());
            if (address == 0x10078) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, 0x82002030); return Task.FromResult(bytes); }
            throw new InvalidOperationException("Unexpected memory read.");
        }
        var caller = await PowerPcUnwinder.UnwindAsync(registers, new(FunctionType.NoHandler, 0x82001000, 0x82001010, 0x82001030), Read);
        Assert.Equal(0x10080UL, caller!.Get64(Register64.r1));
        Assert.Equal(0x82002030u, caller.Get32(Register32.iar));
    }

    private static byte[] Words(params uint[] instructions)
    {
        var bytes = new byte[instructions.Length * 4];
        for (var i = 0; i < instructions.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), instructions[i]);
        return bytes;
    }
    private static byte[] VectorBytes(Vector4 vector) => Words([.. new[] { vector.X, vector.Y, vector.Z, vector.W }.Select(v => unchecked((uint)BitConverter.SingleToInt32Bits(v)))]);
}
