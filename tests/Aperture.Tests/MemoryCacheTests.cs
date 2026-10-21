using Aperture.Memory;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;
public sealed class MemoryCacheTests
{
    [Fact]
    public async Task CacheReturnsCopiesAndInvalidatesOverlappingRanges()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal("getmem addr=0x82000000 length=0x4", await DebugMonitorServer.ReadLine(s, ct));
                await DebugMonitorServer.Write(s, i == 0 ? "202- data\r\n01020304\r\n.\r\n" : "202- data\r\n05060708\r\n.\r\n", ct);
            }
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var memory = new ConsoleMemory(connection) { CacheEnabled = true };
        var first = await memory.ReadAsync(0x82000000, 4); first[0] = 255;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await memory.ReadAsync(0x82000000, 4));
        memory.InvalidateCache(0x82000002, 1);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, await memory.ReadAsync(0x82000000, 4));
    }
}
