using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace Aperture.Consoles;

/// <summary>A self-contained desktop browser window served only on loopback. Dispose the session when the window is no longer needed.</summary>
public sealed class ConsoleDesktopSession : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly Func<HttpListenerContext, CancellationToken, Task> handler;
    private readonly XboxConsole? ownedConsole;
    private TaskCompletionSource<string>? selection;
    public Task<string> SelectionResult => selection?.Task ?? throw new InvalidOperationException("This is not a console selection session.");
    private int disposed;
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public Uri Url { get; }
    private ConsoleDesktopSession(Func<HttpListenerContext, CancellationToken, Task> handler, Action<Uri>? launchBrowser, XboxConsole? ownedConsole = null)
    {
        this.handler = handler; this.ownedConsole = ownedConsole;
        using var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var authority = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(authority); listener.Start();
        Url = new(authority + token + "/");
        worker = RunAsync();
        try { (launchBrowser ?? (url => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })))(Url); }
        catch { stop.Cancel(); listener.Close(); throw; }
    }
    internal static ConsoleDesktopSession Explorer(XboxConsole console, string rootPath, Action<Uri>? launchBrowser)
    {
        var root = ValidateRemotePath(rootPath).TrimEnd('\\') + "\\";
        return new(async (context, ct) =>
        {
            var path = ValidateRemotePath(context.Request.QueryString["path"] ?? root);
            if (!path.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The requested path is outside this browser's root.");
            if (context.Request.HttpMethod == "POST" && context.Request.Url!.AbsolutePath.EndsWith("/upload", StringComparison.Ordinal))
            {
                var name = context.Request.QueryString["name"] ?? "";
                var target = ChildPath(path, name);
                var length = context.Request.ContentLength64;
                if (length is < 0 or > 64 * 1024 * 1024) throw new ArgumentException("Uploads require a known file size of at most 64 MiB.");
                var data = new byte[checked((int)length)];
                await context.Request.InputStream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
                await using var source = new MemoryStream(data, writable: false);
                await console.Files.UploadAsync(source, target, cancellationToken: ct).ConfigureAwait(false);
                await HtmlAsync(context, "File uploaded", "<h1>File uploaded</h1><p>" + Encode(name) + "</p><a href=\"./?path=" + Uri.EscapeDataString(path) + "\">Return to folder</a>", ct).ConfigureAwait(false);
                return;
            }
            if (context.Request.HttpMethod != "GET") { context.Response.StatusCode = 405; return; }
            if (context.Request.Url!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
            {
                var info = await console.Files.GetInfoAsync(path, ct).ConfigureAwait(false);
                if (info.IsDirectory) throw new ArgumentException("Select a file to download.");
                context.Response.ContentType = "application/octet-stream";
                context.Response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(path[(path.LastIndexOf('\\') + 1)..]);
                context.Response.ContentLength64 = checked((long)info.Size);
                await console.Files.DownloadAsync(path, context.Response.OutputStream, ct).ConfigureAwait(false);
                return;
            }
            var directory = path.TrimEnd('\\') + "\\";
            var entries = await console.Files.ListAsync(directory, ct).ConfigureAwait(false);
            var rows = string.Join("", entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(entry =>
            {
                var child = ChildPath(directory, entry.Name);
                var action = entry.IsDirectory ? "./?path=" : "download?path=";
                return "<tr><td><a href=\"" + action + Uri.EscapeDataString(child) + "\">" + Encode(entry.Name) + "</a></td><td>" + (entry.IsDirectory ? "Folder" : entry.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes") + "</td><td>" + (entry.IsDirectory ? "Open folder" : "Download") + "</td></tr>";
            }));
            var parent = directory.Equals(root, StringComparison.OrdinalIgnoreCase) ? "" : "<p><a href=\"./?path=" + Uri.EscapeDataString(directory.TrimEnd('\\')[..(directory.TrimEnd('\\').LastIndexOf('\\') + 1)]) + "\">Parent folder</a></p>";
            var upload = "<form id=\"upload\" data-path=\"" + Encode(directory) + "\"><label>Upload a new file<input id=\"file\" type=\"file\" required></label><button>Upload new file</button><p id=\"upload-status\" role=\"status\">Existing files are preserved. Maximum size: 64 MiB.</p></form><script>document.getElementById('upload').addEventListener('submit',async event=>{event.preventDefault();const form=event.currentTarget,status=document.getElementById('upload-status'),file=document.getElementById('file').files[0];if(!file)return;if(file.size>67108864){status.textContent='Choose a file smaller than 64 MiB.';return;}const button=form.querySelector('button');button.disabled=true;status.textContent='Uploading…';try{const url=new URL('upload',location.href);url.searchParams.set('path',form.dataset.path);url.searchParams.set('name',file.name);const response=await fetch(url,{method:'POST',body:file});const html=await response.text();document.open();document.write(html);document.close();}catch(error){status.textContent=error.message;button.disabled=false;}});</script>";
            await HtmlAsync(context, "Console files", "<h1>" + Encode(console.Name) + " / Files</h1><form method=\"get\"><label>Remote folder<input name=\"path\" value=\"" + Encode(directory) + "\"></label><button>Open folder</button></form>" + parent + "<table><thead><tr><th>Name</th><th>Size</th><th>Action</th></tr></thead><tbody>" + rows + "</tbody></table>" + (entries.Count == 0 ? "<p>This folder is empty.</p>" : "") + upload, ct).ConfigureAwait(false);
        }, launchBrowser, console);
    }
    private static string ValidateRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(c => char.IsControl(c) || c is '/' or '"') || path.IndexOf(':') is not (> 0) || path.IndexOf(':') != path.LastIndexOf(':')) throw new ArgumentException("Use an absolute console path such as Hdd:\\game.");
        var colon = path.IndexOf(':');
        if (path.Length > colon + 1 && path[colon + 1] != '\\') throw new ArgumentException("Use an absolute console path.");
        if (path[(colon + 1)..].Split('\\').Any(p => p is "." or "..")) throw new ArgumentException("Relative path components are not allowed.");
        return path;
    }
    private static string ChildPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Any(c => char.IsControl(c) || c is '\\' or '/' or ':' or '"')) throw new InvalidDataException("The monitor returned an invalid file name.");
        return directory.TrimEnd('\\') + "\\" + name;
    }
    internal static ConsoleDesktopSession Wizard(ConsoleManager manager, Action<Uri>? launchBrowser)
    {
        return new(async (context, ct) =>
        {
            string? message = null;
            if (context.Request.HttpMethod == "POST")
            {
                if (!context.Request.Url!.AbsolutePath.EndsWith("/save", StringComparison.Ordinal)) { context.Response.StatusCode = 404; return; }
                if (context.Request.ContentLength64 is < 0 or > 8192) throw new ArgumentException("Console details exceed the limit.");
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var form = ParseForm(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
                var host = form.GetValueOrDefault("host") ?? "";
                if (Uri.CheckHostName(host) == UriHostNameType.Unknown) throw new ArgumentException("Enter a console name or IP address.");
                manager.AddConsole(host);
                if (form.GetValueOrDefault("default") == "true") manager.SetDefaultConsole(host);
                message = "Console saved: " + host;
            }
            var options = string.Join("", manager.Consoles.Select(host => "<option value=\"" + Encode(host) + "\">"));
            await HtmlAsync(context, "Add console", "<h1>Add console</h1><p>Connect your workbench to an Xbox 360 console.</p>" + (message is null ? "" : "<p role=\"status\">" + Encode(message) + "</p>") + "<form method=\"post\" action=\"save\"><label>Console name or IP address<input name=\"host\" required list=\"known\" placeholder=\"192.168.1.20\"></label><datalist id=\"known\">" + options + "</datalist><label class=\"check\"><input type=\"checkbox\" name=\"default\" value=\"true\"> Use as default console</label><button>Save console</button></form>", ct).ConfigureAwait(false);
        }, launchBrowser);
    }
    internal static ConsoleDesktopSession Select(IReadOnlyList<string> candidates, Action<Uri>? launchBrowser)
    {
        var choices = candidates.ToArray();
        if (choices.Length == 0) throw new InvalidOperationException("No console is available to select.");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ConsoleDesktopSession(async (context, ct) =>
        {
            if (context.Request.HttpMethod == "POST")
            {
                if (context.Request.ContentLength64 is < 0 or > 8192) throw new ArgumentException("Selection details exceed the limit.");
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var form = ParseForm(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
                if (context.Request.Url!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal))
                {
                    await HtmlAsync(context, "Selection cancelled", "<h1>Selection cancelled</h1><p>You can close this window.</p>", ct).ConfigureAwait(false);
                    completion.TrySetCanceled(); return;
                }
                if (!context.Request.Url.AbsolutePath.EndsWith("/select", StringComparison.Ordinal)) { context.Response.StatusCode = 404; return; }
                var host = form.GetValueOrDefault("host") ?? "";
                if (!choices.Contains(host, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a console from the eligible list.");
                await HtmlAsync(context, "Console selected", "<h1>Console selected</h1><p>" + Encode(host) + "</p><p>You can close this window.</p>", ct).ConfigureAwait(false);
                completion.TrySetResult(choices.First(c => c.Equals(host, StringComparison.OrdinalIgnoreCase))); return;
            }
            var options = string.Join("", choices.Select(host => "<option value=\"" + Encode(host) + "\">" + Encode(host) + "</option>"));
            await HtmlAsync(context, "Select console", "<h1>Select console</h1><p>Choose an Xbox 360 console for this operation.</p><form method=\"post\" action=\"select\"><label>Console<select name=\"host\" required>" + options + "</select></label><button>Select console</button></form><form method=\"post\" action=\"cancel\"><p><button>Cancel</button></p></form>", ct).ConfigureAwait(false);
        }, launchBrowser);
        session.selection = completion;
        return session;
    }
    private async Task RunAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(stop.Token).ConfigureAwait(false); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or HttpListenerException) { break; }
            try
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
                if (!context.Request.Url!.AbsolutePath.StartsWith("/" + token + "/", StringComparison.Ordinal)) context.Response.StatusCode = 404;
                else if (context.Request.HttpMethod != "GET" && context.Request.HttpMethod != "POST") context.Response.StatusCode = 405;
                else if (context.Request.HttpMethod == "POST" && context.Request.Headers["Origin"] != Url.GetLeftPart(UriPartial.Authority)) context.Response.StatusCode = 403;
                else await handler(context, stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception e)
            {
                context.Response.StatusCode = 400;
                try { await HtmlAsync(context, "Console operation failed", "<h1>Operation failed</h1><p>" + Encode(e.Message) + "</p><a href=\"./\">Return to the workbench</a>", stop.Token).ConfigureAwait(false); } catch (Exception) { }
            }
            finally
            {
                try { context.Response.Close(); }
                catch (Exception e) when (e is ObjectDisposedException or HttpListenerException or IOException) { }
            }
        }
    }
    private static Dictionary<string, string> ParseForm(string body) => body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => p.Length > 1 ? WebUtility.UrlDecode(p[1]) : "", StringComparer.Ordinal);
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static async Task HtmlAsync(HttpListenerContext context, string title, string content, CancellationToken ct)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        var html = "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>" + Encode(title) + " · Aperture</title><style>body{margin:0;background:#f4f7fa;color:#193149;font:16px 'Segoe UI',sans-serif}main{max-width:960px;margin:48px auto;padding:24px}header{border-bottom:3px solid #1769aa;padding-bottom:14px;font-size:22px;font-weight:600}h1{font-size:32px;margin-top:36px}p{line-height:1.6}label{display:block;margin:22px 0}input:not([type=checkbox]){display:block;box-sizing:border-box;width:100%;max-width:640px;margin-top:8px;padding:12px;border:1px solid #61758a;font:16px Consolas,monospace}button{padding:12px 22px;background:#1769aa;color:white;border:0;font:inherit;cursor:pointer}a{color:#1769aa}input:focus,button:focus,a:focus{outline:3px solid #61758a;outline-offset:3px}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:14px 10px;border-bottom:1px solid #c5d1dc}code{font:15px Consolas,monospace}.check{display:flex;gap:10px}</style><main><header>Aperture / Console workbench</header>" + content + "</main></html>";
        await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(html), ct).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel(); listener.Close();
        selection?.TrySetCanceled();
        try { await worker.ConfigureAwait(false); }
        finally { if (ownedConsole is not null) await ownedConsole.DisposeAsync().ConfigureAwait(false); stop.Dispose(); }
    }
}
