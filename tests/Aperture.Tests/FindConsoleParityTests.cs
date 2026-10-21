using System.Net;
using System.Net.Sockets;
using Aperture.Consoles;
using Xunit;
namespace Aperture.Tests;
public sealed class FindConsoleParityTests
{
 [Fact] public async Task FindRetriesDiscoveryAndMatchesNameWithoutCase()
 {
  using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
  var destination=(IPEndPoint)udp.Client.LocalEndPoint!;
  var responder=Task.Run(async()=>{await udp.ReceiveAsync();var second=await udp.ReceiveAsync();await udp.SendAsync(new byte[]{2,3,(byte)'K',(byte)'I',(byte)'T'},second.RemoteEndPoint);});
  var find=typeof(ConsoleDiscovery).GetMethod("FindAsync");Assert.NotNull(find);
  var result=await (Task<DiscoveredConsole>)find.Invoke(null,["kit",new ConsoleConnectOptions{Attempts=2,RetryDelay=TimeSpan.Zero},TimeSpan.FromMilliseconds(50),destination,CancellationToken.None])!;
  Assert.Equal("KIT",result.Name);Assert.Equal(IPAddress.Loopback,result.Address);await responder;
 }
}
