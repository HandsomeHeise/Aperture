using Aperture.Imaging;
using Xunit;

namespace Aperture.Tests;
public sealed class ScreenshotTests
{
    [Fact]
    public void ScreenshotWirePixelsAreAlreadyLittleEndianDespiteGpuFormatFlags()
    {
        var frame = new ScreenshotFrame(1, 1, 4, 0x18280086, 0, 0, [0, 0, 255, 255]);
        var bmp = frame.ToBmp();
        Assert.Equal((byte)'B', bmp[0]); Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(-1, BitConverter.ToInt32(bmp, 22));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, bmp[54..]);
    }
}
