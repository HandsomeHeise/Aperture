using System.Globalization;
using System.Reflection;
using Xunit;

namespace Aperture.Tests;

public sealed class CodecTests
{
    [Fact]
    public void BigEndianPreserves64BitValuesAndFloatingBits()
    {
        var type = typeof(Aperture.Transport.ConsoleConnection).Assembly.GetType("Aperture.Memory.BigEndianCodec");
        Assert.NotNull(type);
        var write = type!.GetMethod("Encode")!.MakeGenericMethod(typeof(ulong));
        Assert.Equal(new byte[] { 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 }, (byte[])write.Invoke(null, [0xFEDCBA9876543210UL])!);
        var decode = type.GetMethod("Decode")!.MakeGenericMethod(typeof(long));
        Assert.Equal(long.MinValue, decode.Invoke(null, [new byte[] { 128, 0, 0, 0, 0, 0, 0, 0 }]));
        var encodeFloat = type.GetMethod("Encode")!.MakeGenericMethod(typeof(float));
        Assert.Equal(new byte[] { 63, 128, 0, 0 }, encodeFloat.Invoke(null, [1f]));
    }

    [Fact]
    public void RpcWireIsInvariantAndDistinguishesArgumentTypes()
    {
        var type = typeof(Aperture.Transport.ConsoleConnection).Assembly.GetType("Aperture.Rpc.RpcWire");
        Assert.NotNull(type);
        var build = type!.GetMethod("BuildCall")!;
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var command = (string)build.Invoke(null, [0x82000000u, null, 0u, 1, true, 0u, new object[] { -1, 1.5f, "AB", new byte[] { 0, 255 }, ulong.MaxValue }])!;
            Assert.Equal("consolefeatures ver=2 type=1 system as=0 params=\"A\\82000000\\A\\5\\1\\-1\\3\\1.5\\7/2\\4142\\7/2\\00FF\\8\\18446744073709551615\\\"", command);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void ProtocolFieldsParseQuotedNamesAndUnsignedThreadIds()
    {
        var type = typeof(Aperture.Transport.ConsoleConnection).Assembly.GetType("Aperture.Transport.ProtocolFields");
        Assert.NotNull(type);
        var parse = type!.GetMethod("Parse")!;
        var fields = (IReadOnlyDictionary<string, string>)parse.Invoke(null, ["name=\"folder with spaces\" base=0x82000000 directory readonly=1"])!;
        Assert.Equal("folder with spaces", fields["name"]);
        Assert.Equal("", fields["directory"]);
        Assert.Equal(0xFB000008u, type.GetMethod("UInt32")!.Invoke(null, ["-83886072"]));
    }
}
