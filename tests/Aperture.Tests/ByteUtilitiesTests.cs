using Aperture.Memory;
using Xunit;

namespace Aperture.Tests;
public sealed class ByteUtilitiesTests
{
    [Fact]
    public void StringConversionsTerminateAndKeepFullUtf16Characters()
    {
        Assert.Equal("41E4", ByteUtilities.ToHexString("Aä"));
        Assert.Equal(new byte[] { 65, 228, 0 }, ByteUtilities.ToByteArray("Aä"));
        Assert.Equal(new byte[] { 0, 65, 0x20, 0xAC, 0, 0 }, ByteUtilities.ToWchar("A€"));
        Assert.Equal(-1, ByteUtilities.Find("a", "ab"));
        Assert.Equal(-1, ByteUtilities.Find("a", ""));
    }
}
