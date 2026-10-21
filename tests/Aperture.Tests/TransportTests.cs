using System.Text;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;

public sealed class TransportTests
{
    [Fact]
    public async Task SeparateConsolesDoNotShareCommandsOrResponses()
    {
        await using var firstServer = new DebugMonitorServer(async (stream, ct) =>
        {
            Assert.Equal("alpha", await DebugMonitorServer.ReadLine(stream, ct));
            await DebugMonitorServer.Write(stream, "200- first\r\n", ct);
        });
        await using var secondServer = new DebugMonitorServer(async (stream, ct) =>
        {
            Assert.Equal("beta", await DebugMonitorServer.ReadLine(stream, ct));
            await DebugMonitorServer.Write(stream, "200- second\r\n", ct);
        });
        await using var first = await ConsoleConnection.ConnectAsync("127.0.0.1", firstServer.Port);
        await using var second = await ConsoleConnection.ConnectAsync("127.0.0.1", secondServer.Port);
        var replies = await Task.WhenAll(first.ExecuteAsync("alpha"), second.ExecuteAsync("beta"));
        Assert.Equal("first", replies[0].Message);
        Assert.Equal("second", replies[1].Message);
    }
    [Fact]
    public async Task ChunkedMemoryConsumesTerminalHeaderAndReportsPartialRead()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            await DebugMonitorServer.ReadLine(s, ct);
            await DebugMonitorServer.Write(s, "203- binary\r\n", ct);
            await s.WriteAsync(new byte[] { 2, 0, 13, 10, 1, 128, 255 }, ct);
            await DebugMonitorServer.ReadLine(s, ct);
            await DebugMonitorServer.Write(s, "200- intact\r\n", ct);
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var method = typeof(ConsoleConnection).GetMethod("ReceiveChunkedMemoryAsync")!;
        Assert.NotNull(method);
        var task = (Task<byte[]>)method.Invoke(client, ["getmemex addr=0x1 length=0x4", 4, CancellationToken.None])!;
        Assert.Equal(new byte[] { 13, 10, 255 }, await task);
        Assert.Equal("intact", (await client.ExecuteAsync("dbgname")).Message);
    }
    [Fact]
    public async Task EmptyErrorMessageIsAStatusErrorAndDoesNotDesynchronize()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("systime", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "406-\r\n", ct);
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- Jtag\r\n", ct);
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var error = await Assert.ThrowsAsync<ApertureProtocolException>(() => client.ExecuteAsync("systime"));
        Assert.Equal(406, error.StatusCode);
        Assert.Equal("Jtag", (await client.ExecuteAsync("dbgname")).Message);
    }
    [Fact]
    public async Task ReadsFragmentedMultilineAndPreservesNextReply()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct));
            foreach (var b in Encoding.ASCII.GetBytes("202- multiline\r\nname=\"xam.xex\" base=0x82000000\r\n.\r\n")) await s.WriteAsync(new byte[] { b }, ct);
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- Test\r\n", ct);
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var response = await client.ExecuteAsync("modules");
        Assert.Equal(202, response.StatusCode);
        Assert.Equal("name=\"xam.xex\" base=0x82000000", Assert.Single(response.Lines));
        Assert.Equal("Test", (await client.ExecuteAsync("dbgname")).Message);
    }

    [Fact]
    public async Task LengthPrefixedBinaryIsNotTreatedAsText()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("getfile name=\"test\"", await DebugMonitorServer.ReadLine(s, ct));
            await s.WriteAsync(Encoding.ASCII.GetBytes("203- binary\r\n").Concat(new byte[] { 4, 0, 0, 0, 13, 10, 0, 255 }).ToArray(), ct);
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- intact\r\n", ct);
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal(new byte[] { 13, 10, 0, 255 }, await client.ReceiveLengthPrefixedAsync("getfile name=\"test\""));
        Assert.Equal("intact", (await client.ExecuteAsync("dbgname")).Message);
    }

    [Fact]
    public async Task UploadConsumesFinalStatusAndKeepsConnectionAligned()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("sendfile name=\"t\" length=0x3", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "204- send binary\r\n", ct);
            var data = new byte[3]; await s.ReadExactlyAsync(data, ct); Assert.Equal(new byte[] { 1, 13, 10 }, data);
            await DebugMonitorServer.Write(s, "200- saved\r\n", ct);
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "200- ok\r\n", ct);
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal("saved", (await client.SendBinaryAsync("sendfile name=\"t\" length=0x3", new byte[] { 1, 13, 10 })).Message);
        Assert.Equal("ok", (await client.ExecuteAsync("dbgname")).Message);
    }

    [Fact]
    public async Task CancellationDuringBinaryClosesTheConnection()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            await DebugMonitorServer.ReadLine(s, ct);
            await DebugMonitorServer.Write(s, "203- binary\r\n", ct);
            await s.WriteAsync(new byte[] { 1 }, ct);
            await Task.Delay(Timeout.Infinite, ct);
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReceiveBinaryAsync("getmem2 addr=0x1 length=0x2", 2, cancellation.Token));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConcurrentCommandsStaySerialized()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            for (var i = 0; i < 10; i++)
            {
                var command = await DebugMonitorServer.ReadLine(s, ct);
                await DebugMonitorServer.Write(s, "200- " + command + "\r\n", ct);
            }
        });
        await using var client = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var result = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i => (await client.ExecuteAsync("cmd" + i)).Message));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => "cmd" + i), result);
    }
}
