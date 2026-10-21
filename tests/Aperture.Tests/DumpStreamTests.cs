using System.Buffers.Binary;
using Aperture.Dumps;
using Xunit;
namespace Aperture.Tests;
public sealed class DumpStreamTests
{
    [Fact]
    public void ReaderExposesStreamsOutsideCoreListsWithoutDiscardingBytes()
    {
        var bytes = new byte[49];
        "MDMP"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0xA793);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 32);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), 0x304);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 0x47670001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 44);
        new byte[] { 0, 255, 1, 2, 3 }.CopyTo(bytes, 44);
        var path = DumpTests.Temporary();
        try
        {
            File.WriteAllBytes(path, bytes);
            using var reader = new DumpReader(path);
            Assert.Equal(0x304UL, reader.Flags);
            Assert.Equal(0x47670001u, Assert.Single(reader.Streams).Type);
            Assert.Equal(new byte[] { 0, 255, 1, 2, 3 }, reader.ReadStream(0x47670001));
            Assert.Throws<KeyNotFoundException>(() => reader.ReadStream(6));
        }
        finally { File.Delete(path); }
    }
}
