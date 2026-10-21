using Aperture.Consoles;
using Xunit;
namespace Aperture.Tests;
public sealed class SharedConnectionPolicyTests
{
 [Fact] public async Task DirectConnectionRejectsManagerOnlySharedPolicyBeforeNetworkAccess()
 {
  await Assert.ThrowsAsync<ArgumentException>(()=>XboxConsole.ConnectWithOptionsAsync("127.0.0.1",new ConsoleConnectOptions{Shared=true},1));
 }
}
