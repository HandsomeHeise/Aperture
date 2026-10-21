using Aperture.Consoles;
using Aperture.Models;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class ConsoleParityTests
{
    [Fact]
    public async Task ManagerOpenedConsoleRetainsItsManager()
    {
        await using var server = new DebugMonitorServer(async (s, ct) => { Assert.Equal("dbgname", await DebugMonitorServer.ReadLine(s,ct)); await DebugMonitorServer.Write(s,"200- devkit\r\n",ct); await Task.Delay(200,ct); });
        var manager = new ConsoleManager(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
        var method = typeof(ConsoleManager).GetMethods().FirstOrDefault(m => m.Name == "OpenConsoleWithOptionsAsync");
        Assert.NotNull(method);
        await using var console = await (Task<XboxConsole>)method.Invoke(manager,["127.0.0.1",new ConsoleConnectOptions(),server.Port,CancellationToken.None])!;
        Assert.Same(manager,typeof(XboxConsole).GetProperty("Manager")!.GetValue(console));
    }
    [Theory]
    [InlineData(RebootFlags.Cold | RebootFlags.Wait, "magicboot title=\"game.xex\" directory=\"Hdd:\\game\" cmdline=\"-debug\" cold wait")]
    [InlineData(RebootFlags.Warm | RebootFlags.Wait | RebootFlags.Stop, "magicboot title=\"game.xex\" directory=\"Hdd:\\game\" cmdline=\"-debug\" cold stop")]
    public async Task FlagRebootIncludesCommandLineAndNativeFlagPrecedence(RebootFlags flags,string expected)
    {
        await using var server = new DebugMonitorServer(async (s,ct) => { Assert.Equal("dbgname",await DebugMonitorServer.ReadLine(s,ct)); await DebugMonitorServer.Write(s,"200- devkit\r\n",ct); Assert.Equal(expected,await DebugMonitorServer.ReadLine(s,ct)); await DebugMonitorServer.Write(s,"200- OK\r\n",ct); });
        await using var console=await XboxConsole.ConnectAsync("127.0.0.1",server.Port);
        var method=typeof(XboxConsole).GetMethods().FirstOrDefault(m=>m.Name=="RebootAsync" && m.GetParameters().Any(p=>p.ParameterType==typeof(RebootFlags)));
        Assert.NotNull(method);
        await (Task)method.Invoke(console,["game.xex",@"Hdd:\game","-debug",flags,CancellationToken.None])!;
    }
}
