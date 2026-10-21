using Aperture.Consoles;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;
namespace Aperture.Tests;
public sealed class MutableUserParityTests
{
 [Fact] public async Task RenamingLiveUserRetainsRightsAndUpdatesObjectAfterRemoteSuccess()
 {
  await using var server=new DebugMonitorServer(async(s,ct)=>{
   foreach(var pair in new[]{("userlist","202- users\r\nname=\"old\" read manage\r\n.\r\n"),("userlist","202- users\r\nname=\"old\" read manage\r\n.\r\n"),("user name=\"new\"","200- OK\r\n"),("setuserpriv name=\"new\" read manage","200- OK\r\n"),("user name=\"old\" remove","200- OK\r\n")}) {Assert.Equal(pair.Item1,await DebugMonitorServer.ReadLine(s,ct));await DebugMonitorServer.Write(s,pair.Item2,ct);}
  });
  await using var connection=await ConsoleConnection.ConnectAsync("127.0.0.1",server.Port);
  var user=(await new UserAccessClient(connection).ListAsync()).Single();
  var rename=typeof(ConsoleUser).GetMethod("RenameAsync");Assert.NotNull(rename);
  await (Task)rename.Invoke(user,["new",CancellationToken.None])!;
  Assert.Equal("new",user.Name);Assert.Equal(UserPrivileges.Read|UserPrivileges.Manage,user.Privileges);
 }
}

