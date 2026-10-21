using System.Net;
using System.Net.Sockets;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;

public sealed class NotificationTests
{
    [Fact]
    public void DebugStringTextMayContainArbitraryProtocolPunctuation()
    {
        var message = Aperture.Debugging.DebugNotification.Parse("debugstr thread=0xFB000008 string=x = y name=\"broken");
        Assert.Equal("x = y name=\"broken", message.Text);
        Assert.Equal(0xFB000008u, message.ThreadId);
    }
    [Fact]
    public async Task ReverseConnectionDeliversEventsAndUnregistersOnDispose()
    {
        await using var server = new DebugMonitorServer(async (stream, ct) =>
        {
            var fields = ProtocolFields.Parse(await DebugMonitorServer.ReadLine(stream, ct));
            var port = (int)fields.GetUInt32("port");
            using var reverse = new TcpClient();
            await reverse.ConnectAsync(IPAddress.Loopback, port, ct);
            await DebugMonitorServer.Write(stream, "200- registered\r\n", ct);
            await DebugMonitorServer.Write(reverse.GetStream(), "execution started\r\ndebugstr thread=0xFB000008 string=hello world\r\n", ct);
            Assert.Equal($"notifyat port={port} drop", await DebugMonitorServer.ReadLine(stream, ct));
            await DebugMonitorServer.Write(stream, "200- removed\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var type = typeof(ConsoleConnection).Assembly.GetType("Aperture.Debugging.NotificationSession", true)!;
        var session = await (Task<Aperture.Debugging.NotificationSession>)type.GetMethod("SubscribeAsync")!.Invoke(null, [connection, IPAddress.Loopback, CancellationToken.None])!;
        await using (session)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var events = session.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
            Assert.True(await events.MoveNextAsync());
            Assert.Equal(Aperture.Models.DebugEventType.ExecStateChange, events.Current.Type);
            Assert.True(await events.MoveNextAsync());
            Assert.Equal("hello world", events.Current.Text);
            Assert.Equal(0xFB000008u, events.Current.ThreadId);
        }
    }
}
