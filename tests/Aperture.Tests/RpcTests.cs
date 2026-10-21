using Aperture.Rpc;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;

public sealed class RpcTests
{
    [Theory]
    [InlineData("DEBUG build")]
    [InlineData("last error=none")]
    public async Task StringReturnsMayContainDiagnosticWords(string value)
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            await DebugMonitorServer.ReadLine(s, ct);
            await DebugMonitorServer.Write(s, "200- " + value + "\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(value, await new JrpcClient(connection).CallStringAsync(0x82000000, []));
    }
    [Fact]
    public async Task PollsReturnedBufferAndParsesSignedScalar()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("consolefeatures ver=2 type=1 system as=0 params=\"A\\82000000\\A\\0\\\"", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- buf_addr=90000000\r\n", ct);
            Assert.Equal("consolefeatures buf_addr=0x90000000", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- FFFFFFFF\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(-1, await new JrpcClient(connection).CallAsync<int>(0x82000000, []));
    }

    [Fact]
    public async Task ParsesFull64BitArrayInsteadOfTruncatingOrUsingFixedLength()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("consolefeatures ver=2 type=9 system as=2 params=\"A\\82000000\\A\\0\\\"", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- FEDCBA9876543210,FFFFFFFFFFFFFFFF;\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(new ulong[] { 0xFEDCBA9876543210, ulong.MaxValue }, await new JrpcClient(connection).CallArrayAsync<ulong>(0x82000000, 2, []));
    }

    [Fact]
    public void FeatureStringsUseFeatureTypeAndCallLimitIsEnforced()
    {
        Assert.Equal("consolefeatures ver=2 type=12 params=\"A\\0\\A\\2\\2/2\\4142\\1\\34\\\"", RpcWire.BuildFeature(12, 0, "AB", 34));
        Assert.Throws<ArgumentException>(() => RpcWire.BuildCall(1, null, 0, 1, true, 0, Enumerable.Repeat<object>(1, 38).ToArray()));
        Assert.Throws<ArgumentException>(() => RpcWire.BuildCall(1, null, 0, 1, true, 0, [new object()]));
    }
}
