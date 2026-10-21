using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;

public sealed class ConsoleTests
{
    [Fact]
    public void EmptyMonitorErrorsRemainActionableAndPreserveRawResponse()
    {
        var error = new ApertureProtocolException(406, "");
        Assert.Contains("clock", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", error.ResponseMessage);
        Assert.Contains("clock", Aperture.Consoles.ConsoleManager.TranslateError(unchecked((int)0x82DA0006)), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task ConnectPolicyRetriesDisconnectedGreetingAndSetsConversationTimeout()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var worker = Task.Run(async () =>
        {
            using (var first = await listener.AcceptTcpClientAsync(deadline.Token)) { }
            using var second = await listener.AcceptTcpClientAsync(deadline.Token);
            var stream = second.GetStream();
            await DebugMonitorServer.Write(stream, "201- connected\r\n", deadline.Token);
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(stream, deadline.Token));
            await DebugMonitorServer.Write(stream, "200- RetryKit\r\n", deadline.Token);
        });
        await using var console = await XboxConsole.ConnectWithOptionsAsync("127.0.0.1", new() { Attempts = 2, RetryDelay = TimeSpan.Zero, ConversationTimeout = TimeSpan.FromSeconds(9) }, port, deadline.Token);
        Assert.Equal("RetryKit", console.Name);
        Assert.Equal(TimeSpan.FromSeconds(9), console.Connection.Timeout);
        await worker;
    }
    [Fact]
    public async Task TitleAddressUsesNativeAlternateAddressCommand()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "200- TestKit\r\n", ct);
            Assert.Equal("altaddr", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "200- addr=0x0A0A0ADA\r\n", ct);
        });
        await using var console = await XboxConsole.ConnectAsync("127.0.0.1", server.Port);
        Assert.Equal("10.10.10.218", (await console.GetTitleIPAddressAsync()).ToString());
    }
    [Fact]
    public async Task ConsoleFacadeUsesIndependentTransportAndReadsLiveServices()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "200- TestKit\r\n", ct);
            Assert.Equal("consolefeatures ver=2 type=13 params=\"A\\0\\A\\0\\\"", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "200- 17559\r\n", ct);
            Assert.Equal("getmem addr=0x80000000 length=0x8", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- memory\r\nFEDCBA9876543210\r\n.\r\n", ct);
        });
        var type = typeof(ConsoleConnection).Assembly.GetType("Aperture.XboxConsole"); Assert.NotNull(type);
        var connect = type!.GetMethod("ConnectAsync")!;
        var task = (Task)connect.Invoke(null, ["127.0.0.1", server.Port, CancellationToken.None])!; await task;
        await using var console = (IAsyncDisposable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        Assert.Equal("TestKit", type.GetProperty("Name")!.GetValue(console));
        var rpc = (Aperture.Rpc.JrpcClient)type.GetProperty("Rpc")!.GetValue(console)!;
        Assert.Equal(17559u, await rpc.GetKernelVersionAsync());
        var memory = (Aperture.Memory.ConsoleMemory)type.GetProperty("Memory")!.GetValue(console)!;
        Assert.Equal(0xFEDCBA9876543210UL, await memory.ReadAsync<ulong>(0x80000000));
    }
}
