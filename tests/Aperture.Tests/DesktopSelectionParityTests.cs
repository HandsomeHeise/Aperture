using Aperture.Consoles;
using Xunit;
namespace Aperture.Tests;

public sealed class DesktopSelectionParityTests
{
    [Fact]
    public async Task ConsoleSelectionHasRealFormAndReturnsOnlyAnEligibleChoice()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-select-" + Guid.NewGuid() + ".json");
        try
        {
            var manager = new ConsoleManager(path); manager.AddConsole("first"); manager.AddConsole("second");
            await using var session = manager.RunConsoleSelection(launchBrowser: _ => { });
            using var http = new HttpClient();
            var html = await http.GetStringAsync(session.Url);
            Assert.Contains("<select", html); Assert.Contains("second", html);
            using var bad = new HttpRequestMessage(HttpMethod.Post, new Uri(session.Url, "select")) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["host"] = "unknown" }) };
            bad.Headers.Add("Origin", session.Url.GetLeftPart(UriPartial.Authority));
            using var rejected = await http.SendAsync(bad); Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode); Assert.False(session.SelectionResult.IsCompleted);
            using var valid = new HttpRequestMessage(HttpMethod.Post, new Uri(session.Url, "select")) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["host"] = "second" }) };
            valid.Headers.Add("Origin", session.Url.GetLeftPart(UriPartial.Authority));
            using var accepted = await http.SendAsync(valid); accepted.EnsureSuccessStatusCode();
            Assert.Equal("second", await session.SelectionResult.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { File.Delete(path); }
    }
}
