using Aperture.Symbols;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;
public sealed class ExecutableClientTests
{
    [Fact]
    public async Task ReadsEntryPointAndUtf8OriginalPeNameFromXexFields()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("xexfield module=\"game.xex\" field=0x00010100", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "202- multiline\r\nfieldsize=4\r\n82001000\r\n.\r\n", ct);
            Assert.Equal("xexfield module=\"game.xex\" field=0x000183FF", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "202- multiline\r\nfieldsize=12\r\n0000000C67616D652E706500\r\n.\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var executable = new ExecutableClient(connection, "game.xex");
        Assert.Equal(0x82001000u, await executable.GetEntryPointAddressAsync());
        Assert.Equal("game.pe", await executable.GetPeModuleNameAsync());
    }
}
