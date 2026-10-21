using Aperture.Consoles;
using System.Net;
using Xunit;
namespace Aperture.Tests;
public sealed class DesktopConsoleParityTests
{
 [Fact] public async Task WizardHasUsableFormAndSavesThroughManager()
 {
  var settings=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
  try {
   var manager=new ConsoleManager(settings);
   var method=typeof(ConsoleManager).GetMethod("RunAddConsoleWizard");Assert.NotNull(method);
   var session=method.Invoke(manager,[new Action<Uri>(_=>{})])!;
   await using var dispose=(IAsyncDisposable)session;
   var url=(Uri)session.GetType().GetProperty("Url")!.GetValue(session)!;
   using var http=new HttpClient();var page=await http.GetStringAsync(url);Assert.Contains("Add console",page);Assert.Contains("<form",page);
   using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(url,"save")){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"host","192.0.2.20"},{"default","true"}})};
   request.Headers.Add("Origin",url.GetLeftPart(UriPartial.Authority));
   using var response=await http.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("192.0.2.20",manager.DefaultConsole);
   using var outsider=await http.GetAsync(url.GetLeftPart(UriPartial.Authority)+"/wrong/");Assert.Equal(HttpStatusCode.NotFound,outsider.StatusCode);
  } finally {if(File.Exists(settings)) File.Delete(settings);}
 }
}
