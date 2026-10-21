using Aperture.Consoles;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;
public sealed class UserAccessTests
{
    [Fact]
    public async Task UserPrivilegesUseNativeNamedFlags()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("getuserpriv name=\"tester\"", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- read control manage\r\n", ct);
            Assert.Equal("setuserpriv name=\"tester\" read write", await DebugMonitorServer.ReadLine(s, ct));
            await DebugMonitorServer.Write(s, "200- OK\r\n", ct);
        });
        await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
        var users = new UserAccessClient(connection);
        Assert.Equal(UserPrivileges.Read | UserPrivileges.Control | UserPrivileges.Manage, await users.GetPrivilegesAsync("tester"));
        await users.SetPrivilegesAsync("tester", UserPrivileges.Read | UserPrivileges.Write);
    }
}
