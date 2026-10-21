using Aperture.Files;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;

public sealed class FileTests
{
    [Fact]
    public async Task DirectoryKeeps64BitSizesNamesAndFlags()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("dirlist name=\"HDD:\\folder with spaces\"", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "202- directory\r\nname=\"large.bin\" sizehi=0x00000001 sizelo=0x00000002 readonly\r\nname=\"nested\" sizehi=0 sizelo=0 directory\r\n.\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var files = await new ConsoleFileSystem(connection).ListAsync("HDD:\\folder with spaces");
        Assert.Equal(0x100000002UL, files[0].Size);
        Assert.True(files[0].IsReadOnly); Assert.True(files[1].IsDirectory);
        Assert.Equal("large.bin", files[0].Name);
    }

    [Fact]
    public async Task ReadsOnlyTheRequestedPartialFileRange()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("getfile name=\"HDD:\\t.bin\" offset=4 size=3", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "203- binary\r\n", ct);
            await s.WriteAsync(new byte[] { 3, 0, 0, 0, 10, 13, 255 }, ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(new byte[] { 10, 13, 255 }, await new ConsoleFileSystem(connection).ReadBytesAsync("HDD:\\t.bin", 4, 3));
    }
}
