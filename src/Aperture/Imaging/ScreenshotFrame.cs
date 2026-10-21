using System.Buffers.Binary;
using Aperture.Transport;

namespace Aperture.Imaging;

public sealed record ScreenshotFrame(int Width, int Height, int Pitch, uint Format, int OffsetX, int OffsetY, byte[] Framebuffer)
{
    public static async Task<ScreenshotFrame> CaptureAsync(string host, int port = 730, CancellationToken cancellationToken = default)
    {
        await using var session = await MonitorSession.ConnectAsync(host, port, cancellationToken: cancellationToken).ConfigureAwait(false);
        var status = await session.SendTextCommandAsync("screenshot", cancellationToken).ConfigureAwait(false);
        if (status.StatusCode != 203) throw new InvalidDataException("Expected screenshot binary response.");
        var fields = ProtocolFields.Parse(await session.ReceiveLineAsync(cancellationToken).ConfigureAwait(false));
        var size = fields.GetUInt32("framebuffersize");
        if (size > 64 * 1024 * 1024) throw new InvalidDataException("Framebuffer exceeds limits.");
        var result = new ScreenshotFrame(checked((int)fields.GetUInt32("width")), checked((int)fields.GetUInt32("height")), checked((int)fields.GetUInt32("pitch")), fields.GetUInt32("format"), checked((int)fields.GetUInt32("offsetx")), checked((int)fields.GetUInt32("offsety")), await session.ReceiveBinaryAsync((int)size, cancellationToken).ConfigureAwait(false));
        result.Validate(); return result;
    }
    public byte[] ToBmp()
    {
        Validate();
        if ((Format & 63) != 6) throw new NotSupportedException($"GPU screenshot format 0x{Format:X8} is not an 8-bit RGBA surface.");
        var bmp = new byte[checked(54 + Width * Height * 4)];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        U32(2, (uint)bmp.Length); U32(10, 54); U32(14, 40); U32(18, (uint)Width); U32(22, unchecked((uint)-Height));
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1); BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 32); U32(34, (uint)(bmp.Length - 54));
        var tiled = (Format & 256) != 0;
        // XBDM transfers normalized little-endian words, even when the GPU format
        // records 8-in-32 endian mode. Native DmScreenShot also leaves these bytes unswapped.
        var selectors = new[] { (int)((Format >> 18) & 7), (int)((Format >> 21) & 7), (int)((Format >> 24) & 7), (int)((Format >> 27) & 7) };
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var sx = x + OffsetX; var sy = y + OffsetY;
                var source = tiled ? TiledByteOffset(sx, sy, Pitch / 4) : checked(sy * Pitch + sx * 4);
                if (source < 0 || source > Framebuffer.Length - 4) throw new InvalidDataException("Screenshot pixel points outside the framebuffer.");
                var destination = 54 + (y * Width + x) * 4;
                bmp[destination] = Component(selectors[2]); bmp[destination + 1] = Component(selectors[1]); bmp[destination + 2] = Component(selectors[0]); bmp[destination + 3] = Component(selectors[3]);
                byte Component(int selector) => selector switch { >= 0 and <= 3 => Framebuffer[source + selector], 4 => 0, 5 => 255, _ => throw new InvalidDataException("Unsupported GPU channel selector.") };
            }
        return bmp;
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(offset, 4), value);
    }
    public async Task SaveBmpAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = ToBmp();
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
    private void Validate()
    {
        if (Width <= 0 || Height <= 0 || Width > 8192 || Height > 8192 || OffsetX < 0 || OffsetY < 0 || Pitch <= 0 || (Pitch & 3) != 0 || Pitch / 4 > 8192 || (long)Width + OffsetX > Pitch / 4 || (long)Height + OffsetY > 8192 || Framebuffer.Length > 64 * 1024 * 1024) throw new InvalidDataException("Invalid framebuffer dimensions.");
        if ((long)Width * Height * 4 > 64 * 1024 * 1024) throw new InvalidDataException("Decoded screenshot exceeds limits.");
    }
    private static int TiledByteOffset(int x, int y, int stridePixels)
    {
        // Xbox GPU address bit layout: 32x32 macrotiles, 8x2 microtiles, then bank interleave.
        var macroTile = ((y / 32) * ((stridePixels + 31) / 32) + x / 32) * 512;
        var microTile = ((x % 8) + (y & 6) * 4) * 4;
        var combined = macroTile + (microTile & ~15) * 2 + (microTile & 15) + (y & 8) * 32 + (y & 1) * 16;
        return (combined & ~511) * 8 + (combined & 448) * 4 + (combined & 63) + (y & 16) * 128 + (((y & 8) / 4 + x / 8) % 4) * 64;
    }
}
