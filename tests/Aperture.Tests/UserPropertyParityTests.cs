using Aperture.Consoles;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class UserPropertyParityTests
{
 [Theory]
 [InlineData(UserPrivileges.Read,"read",nameof(ConsoleUser.HasReadAccess))]
 [InlineData(UserPrivileges.Write,"write",nameof(ConsoleUser.HasWriteAccess))]
 [InlineData(UserPrivileges.Control,"control",nameof(ConsoleUser.HasControlAccess))]
 [InlineData(UserPrivileges.Configure,"config",nameof(ConsoleUser.HasConfigureAccess))]
 [InlineData(UserPrivileges.Manage,"manage",nameof(ConsoleUser.HasManageAccess))]
 public async Task ChangingOneAccessPropertyPreservesRemainingRights(UserPrivileges flag,string token,string property)
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{Assert.Equal("userlist",await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"202- users\r\nname=\"user\" read write control config manage\r\n.\r\n",ct);var tokens=new[]{"read","write","control","config","manage"}.Where(t=>t!=token);Assert.Equal("setuserpriv name=\"user\" "+string.Join(' ',tokens),await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,"200- OK\r\n",ct);});
  await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);
  var user=(await new UserAccessClient(connection).ListAsync()).Single();
  var setter=typeof(ConsoleUser).GetMethod("SetAccessAsync");Assert.NotNull(setter);
  await (Task)setter.Invoke(user,[flag,false,CancellationToken.None])!;
  Assert.Equal((UserPrivileges)31 & ~flag,user.Privileges);Assert.Equal(false,typeof(ConsoleUser).GetProperty(property)!.GetValue(user));
 }
}
