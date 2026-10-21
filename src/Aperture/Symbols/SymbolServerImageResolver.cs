using Aperture.Debugging;
using System.Net;
namespace Aperture.Symbols;

/// <summary>Retrieves PE images from SymSrv stores using filename/timestamp+size/filename keys.</summary>
public static class SymbolServerImageResolver
{
    internal const int MaximumImageBytes = 64 * 1024 * 1024;
    private static readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(60) };
    internal static string DefaultCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aperture", "symbols");
    public static Task<string?> ResolveAsync(ModuleInfo module, IReadOnlyList<string> stores, CancellationToken cancellationToken = default)
        => ResolveAsync(module, stores, TimeSpan.FromSeconds(60), cancellationToken);
    public static async Task<string?> ResolveAsync(ModuleInfo module, IReadOnlyList<string> stores, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return await ResolveCoreAsync(module, stores, deadline.Token).ConfigureAwait(false);
    }
    private static async Task<string?> ResolveCoreAsync(ModuleInfo module, IReadOnlyList<string> stores, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module); ArgumentNullException.ThrowIfNull(stores);
        var name = module.Name[(module.Name.LastIndexOfAny(['\\', '/']) + 1)..];
        if (name.Length == 0 || name is "." or ".." || name.Any(c => char.IsControl(c) || c is ':' or '/' or '\\' || Path.GetInvalidFileNameChars().Contains(c))) throw new ArgumentException("The module has an invalid image filename.", nameof(module));
        var key = $"{module.Timestamp:X8}{module.Size:X}";
        var chain = stores.Select(s => string.IsNullOrWhiteSpace(s) ? DefaultCache : s).ToList();
        if (chain.Count == 0) return null;
        if (IsHttp(chain[0])) chain.Insert(0, DefaultCache);
        for (var index = 0; index < chain.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsHttp(chain[index]) && index != chain.Count - 1) throw new ArgumentException("HTTP symbol stores must be the rightmost store.", nameof(stores));
            var relative = name + "/" + key + "/" + name;
            byte[]? bytes;
            if (IsHttp(chain[index]))
            {
                var upstream = new Uri(chain[index].TrimEnd('/') + "/");
                bytes = await DownloadAsync(new Uri(upstream, EscapePath(relative)), MaximumImageBytes, cancellationToken).ConfigureAwait(false);
                if (bytes is null)
                {
                    var compressed = await DownloadAsync(new Uri(upstream, EscapePath(relative[..^1] + "_")), MaximumImageBytes, cancellationToken).ConfigureAwait(false);
                    if (compressed is not null) bytes = await CompressedSymbolImage.ExpandAsync(compressed, name, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                var local = Path.Combine(Path.GetFullPath(chain[index]), name, key, name);
                if (File.Exists(local))
                {
                    if (new FileInfo(local).Length > MaximumImageBytes) throw new InvalidDataException("The symbol-store image exceeds 64 MiB.");
                    ValidateIdentity(local, module);
                    if (index == 0) return local;
                    if (new FileInfo(local).Length > MaximumImageBytes) throw new InvalidDataException("The symbol-store image exceeds 64 MiB.");
                    bytes = await File.ReadAllBytesAsync(local, cancellationToken).ConfigureAwait(false);
                }
                else if (File.Exists(local[..^1] + "_"))
                {
                    var compressedPath = local[..^1] + "_";
                    if (new FileInfo(compressedPath).Length > MaximumImageBytes) throw new InvalidDataException("The compressed symbol image exceeds 64 MiB.");
                    bytes = await CompressedSymbolImage.ExpandAsync(await File.ReadAllBytesAsync(compressedPath, cancellationToken).ConfigureAwait(false), name, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var pointer = Path.Combine(Path.GetDirectoryName(local)!, "file.ptr");
                    if (!File.Exists(pointer)) continue;
                    if (new FileInfo(pointer).Length > 4096) throw new InvalidDataException("Symbol-store pointer exceeds 4 KiB.");
                    var location = (await File.ReadAllTextAsync(pointer, cancellationToken).ConfigureAwait(false)).Trim();
                    bytes = await ReadPointerAsync(location, cancellationToken).ConfigureAwait(false);
                }
            }
            if (bytes is null && IsHttp(chain[index]))
            {
                var upstream = new Uri(chain[index].TrimEnd('/') + "/");
                var pointer = await DownloadAsync(new Uri(upstream, EscapePath(name + "/" + key + "/file.ptr")), 4096, cancellationToken).ConfigureAwait(false);
                if (pointer is not null) bytes = await ReadPointerAsync(System.Text.Encoding.UTF8.GetString(pointer).Trim(), cancellationToken).ConfigureAwait(false);
            }
            if (bytes is null) continue;
            string? first = null;
            for (var downstream = index == 0 ? 0 : index - 1; downstream >= 0; downstream--)
            {
                var target = Path.Combine(Path.GetFullPath(chain[downstream]), name, key, name);
                await CacheAsync(target, bytes, module, cancellationToken).ConfigureAwait(false);
                first = target;
            }
            return first;
        }
        return null;
    }
    private static async Task<byte[]?> ReadPointerAsync(string location, CancellationToken ct)
    {
        if (location.StartsWith("PATH:", StringComparison.OrdinalIgnoreCase)) location = location[5..].Trim();
        if (location.Length == 0 || location.Any(char.IsControl)) throw new InvalidDataException("Symbol-store pointer is invalid.");
        if (IsHttp(location)) return await DownloadAsync(new Uri(location), MaximumImageBytes, ct).ConfigureAwait(false);
        if (!Path.IsPathFullyQualified(location)) throw new InvalidDataException("Symbol-store pointer must contain an absolute path.");
        if (!File.Exists(location)) return null;
        if (new FileInfo(location).Length > MaximumImageBytes) throw new InvalidDataException("Symbol-store image exceeds 64 MiB.");
        return await File.ReadAllBytesAsync(location, ct).ConfigureAwait(false);
    }
    internal static bool IsHttp(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static string EscapePath(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));
    internal static void ValidateIdentity(string path, ModuleInfo module)
    {
        var image = PeMetadata.Read(path);
        if (image.TimeDateStamp != module.Timestamp || image.SizeOfImage != module.Size) throw new InvalidDataException("The symbol-store image does not match the module timestamp and image size.");
    }
    private static async Task CacheAsync(string target, byte[] bytes, ModuleInfo module, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(target)!; Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".aperture-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            ValidateIdentity(temporary, module); ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static async Task<byte[]?> DownloadAsync(Uri uri, int maximumBytes, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Symbol-server response exceeds the size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > maximumBytes) throw new InvalidDataException("Symbol-server response exceeds the size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

