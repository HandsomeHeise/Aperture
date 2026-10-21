using System.Buffers.Binary;
using Aperture.Symbols;
using Xunit;

namespace Aperture.Tests;

public sealed class PdbIdentityTests
{
    [Fact]
    public void ReadsPortablePdbIdentityFromBuildArtifact()
    {
        var assembly = typeof(XboxConsole).Assembly.Location;
        var metadata = PeMetadata.Read(assembly);
        var identity = PdbIdentity.Read(Path.ChangeExtension(assembly, ".pdb"));
        Assert.Equal(metadata.PdbGuid, identity.Guid);
        Assert.Equal((uint)metadata.PdbAge, identity.Age);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsMsfInformationStreamAndRejectsPageOutsideFile(bool corrupt)
    {
        var bytes = new byte[2048];
        "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8.CopyTo(bytes);
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        U(32, 512); U(40, 4); U(44, 16); U(52, 1); U(512, 2);
        U(1024, 2); U(1028, uint.MaxValue); U(1032, 28); U(1036, corrupt ? 4u : 3u);
        var guid = Guid.Parse("568605de-452b-47d9-b1ca-9af730d4af4e");
        U(1536, 20000404); U(1544, 7); guid.TryWriteBytes(bytes.AsSpan(1548));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdb");
        try
        {
            File.WriteAllBytes(path, bytes);
            if (corrupt) Assert.Throws<InvalidDataException>(() => PdbIdentity.Read(path));
            else Assert.Equal(new PdbIdentity(guid, 7), PdbIdentity.Read(path));
        }
        finally { File.Delete(path); }
    }
}
