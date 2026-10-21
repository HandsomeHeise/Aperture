using System.Buffers.Binary;
using Aperture.Dumps;
using Aperture.Models;
using Xunit;

namespace Aperture.Tests;

public sealed class DumpTests
{
    [Fact]
    public void ContextEncoderRejectsPartialGroupInsteadOfDiscardingRegisters()
    {
        var registers = Aperture.Debugging.RegisterSet.Parse(["Msr=0x10000 Iar=0x82001000 Lr=0x82002000 Ctr=0q1 Gpr1=0q10000 Gpr14=0q2222"]);
        var codec = typeof(DumpReader).Assembly.GetType("Aperture.Dumps.XenonContextCodec", true)!;
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => codec.GetMethod("Encode")!.Invoke(null, [registers]));
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }
    [Fact]
    public void ContextEncoderPreservesFullControlGroupBits()
    {
        var registers = Aperture.Debugging.RegisterSet.Parse(["Msr=0x10000 Iar=0x82001000 Lr=0x82002000 Ctr=0qFEDCBA9876543210"]);
        var codec = typeof(DumpReader).Assembly.GetType("Aperture.Dumps.XenonContextCodec", true)!;
        var encode = codec.GetMethod("Encode")!; Assert.NotNull(encode);
        var bytes = (byte[])encode.Invoke(null, [registers])!;
        Assert.Equal(Fixture()[144..168], bytes[..24]);
        var decode = codec.GetMethod("Decode")!;
        var restored = (Aperture.Debugging.RegisterSet)decode.Invoke(null, [bytes])!;
        Assert.Equal(0xFEDCBA9876543210UL, restored.Get64(Register64.ctr));
        Assert.False(restored.Has64(Register64.r0));
    }
    [Fact]
    public void WriterRoundTripsModulesRegistersAndMemory()
    {
        var path = Temporary();
        try
        {
            var context = Fixture()[144..168];
            var writer = typeof(DumpReader).Assembly.GetType("Aperture.Dumps.DumpWriter", true)!;
            var method = writer.GetMethod("Write")!;
            method.Invoke(null, [path, Array.Empty<Aperture.Debugging.ModuleInfo>(), new[] { new DumpThreadSnapshot(9, 2, 25, 0, context) }, new[] { new DumpMemorySnapshot(0x82000000, new byte[] { 1, 2, 3, 4 }) }]);
            using var reader = new DumpReader(path);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, reader.ReadMemory(0x82000000, 4));
            Assert.Equal(2u, reader.GetThreadInfo(9).SuspendCount);
            Assert.Equal(0x82001000u, reader.GetRegisters(9).Get32(Register32.iar));
            Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [path, Array.Empty<Aperture.Debugging.ModuleInfo>(), Array.Empty<DumpThreadSnapshot>(), Array.Empty<DumpMemorySnapshot>()]));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ThreadStackRemainsReadableWithoutMemoryList()
    {
        var path = Temporary();
        try
        {
            var bytes = Fixture();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
            bytes.AsSpan(44, 12).CopyTo(bytes.AsSpan(32, 12));
            File.WriteAllBytes(path, bytes);
            using var reader = new DumpReader(path);
            Assert.Equal(bytes[168..], reader.ReadMemory(0x82000000, 8));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ReadsContiguousMemoryRangesAndPpcControlRegistersFromMinidump()
    {
        var path = Temporary();
        try
        {
            File.WriteAllBytes(path, Fixture());
            using var reader = new DumpReader(path);
            Assert.Equal(new uint[] { 1234 }, reader.ThreadIds);
            Assert.Equal(new byte[] { 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 }, reader.ReadMemory(0x82000000, 8));
            var registers = reader.GetRegisters(1234);
            Assert.Equal(0x82001000u, registers.Get32(Register32.iar));
            Assert.Equal(0xFEDCBA9876543210UL, registers.Get64(Register64.ctr));
            Assert.Throws<EndOfStreamException>(() => reader.ReadMemory(0x82000004, 8));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void RejectsDirectoryOffsetsOutsideTheFile()
    {
        var path = Temporary();
        try
        {
            var bytes = Fixture(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0xFFFFFFFC); File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => new DumpReader(path));
        }
        finally { File.Delete(path); }
    }
    internal static byte[] Fixture()
    {
        var bytes = new byte[176]; "MDMP"u8.CopyTo(bytes);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
        U32(4, 0xA793); U32(8, 2); U32(12, 32);
        U32(32, 5); U32(36, 36); U32(40, 56);
        U32(44, 3); U32(48, 52); U32(52, 92);
        U32(56, 2); U64(60, 0x82000000); U32(68, 4); U32(72, 168);
        U64(76, 0x82000004); U32(84, 4); U32(88, 172);
        U32(92, 1); U32(96, 1234); U32(108, 25); U64(112, 0x3A000000);
        U64(120, 0x82000000); U32(128, 8); U32(132, 168); U32(136, 24); U32(140, 144);
        U32(144, 1); U32(148, 0x10000); U32(152, 0x82001000); U32(156, 0x82002000); U64(160, 0xFEDCBA9876543210);
        new byte[] { 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 }.CopyTo(bytes, 168);
        return bytes;
    }
    internal static string Temporary() => Path.Combine(Path.GetTempPath(), "aperture-dump-" + Guid.NewGuid().ToString("N") + ".dmp");
}
