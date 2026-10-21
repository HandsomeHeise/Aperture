using Aperture.Debugging;
namespace Aperture.Symbols;

/// <summary>Builds a managed image database from local paths and standard SymSrv/cache directives.</summary>
public static class ImageSearchPathResolver
{
    public static ExecutableDatabase CreateDatabase(string imageSearchPath, IEnumerable<ModuleInfo> modules)
        => CreateDatabaseAsync(imageSearchPath, modules).ConfigureAwait(false).GetAwaiter().GetResult();
    public static async Task<ExecutableDatabase> CreateDatabaseAsync(string imageSearchPath, IEnumerable<ModuleInfo> modules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageSearchPath); ArgumentNullException.ThrowIfNull(modules);
        var paths = imageSearchPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var database = new ExecutableDatabase();
        try
        {
            foreach (var module in modules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = module.Name[(module.Name.LastIndexOfAny(['\\', '/']) + 1)..];
                var stem = Path.GetFileNameWithoutExtension(name);
                ExecutableInfo? matching = null; string? cache = null;
                foreach (var path in paths)
                {
                    var parts = path.Split('*');
                    if (parts[0].Equals("cache", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length > 2) throw new ArgumentException("A cache directive accepts one local path.", nameof(imageSearchPath));
                        cache = parts.Length == 1 || parts[1].Length == 0 ? SymbolServerImageResolver.DefaultCache : parts[1];
                        var cachedImage = await SymbolServerImageResolver.ResolveAsync(module, [cache], cancellationToken).ConfigureAwait(false);
                        if (cachedImage is not null)
                        {
                            var info = PeMetadata.Read(cachedImage);
                            matching = new(info.PdbGuid ?? Guid.NewGuid(), name, cachedImage, "", "", name, info.TimeDateStamp, info.SizeOfImage, false);
                            break;
                        }
                        continue;
                    }
                    string? serverImage = null;
                    var server = parts[0].Equals("srv", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("symsrv", StringComparison.OrdinalIgnoreCase);
                    if (server || SymbolServerImageResolver.IsHttp(path))
                    {
                        var skip = parts[0].Equals("symsrv", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                        if (skip == 2 && (parts.Length < 3 || !parts[1].Equals("symsrv.dll", StringComparison.OrdinalIgnoreCase))) throw new NotSupportedException("Custom symbol-server plugins cannot be loaded; use srv with a standard symbol store.");
                        var stores = server ? parts.Skip(skip).ToList() : new List<string> { path };
                        if (stores.Count == 0) stores.Add(SymbolServerImageResolver.DefaultCache);
                        if (cache is not null && (stores.Count == 0 || !string.Equals(stores[0], cache, StringComparison.OrdinalIgnoreCase))) stores.Insert(0, cache);
                        serverImage = await SymbolServerImageResolver.ResolveAsync(module, stores, cancellationToken).ConfigureAwait(false);
                    }
                    else if (parts.Length > 1) throw new ArgumentException("Unrecognized image-search directive.", nameof(imageSearchPath));
                    var candidates = serverImage is not null ? new[] { serverImage } : server ? Array.Empty<string>() :
                        (Directory.Exists(path) ? Directory.EnumerateFiles(path) : File.Exists(path) ? new[] { path } : Array.Empty<string>())
                        .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), stem, StringComparison.OrdinalIgnoreCase)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
                    foreach (var candidate in candidates)
                    {
                        PeImageInfo info;
                        try { info = PeMetadata.Read(candidate); }
                        catch (BadImageFormatException) { continue; }
                        if (info.TimeDateStamp != module.Timestamp || info.SizeOfImage != module.Size) continue;
                        if (matching is not null && !string.Equals(matching.PortableExecutablePath, candidate, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Multiple local images match module '{name}'.");
                        matching = new(info.PdbGuid ?? Guid.NewGuid(), name, candidate, "", "", name, info.TimeDateStamp, info.SizeOfImage, false);
                    }
                    if (serverImage is not null) break;
                }
                if (matching is null) continue;
                if (database.FindExecutableByGuid(matching.SymbolGuid) is not null) throw new InvalidDataException("Multiple module images share a symbol identity.");
                database.AddExecutable(matching);
            }
            return database;
        }
        catch { database.Dispose(); throw; }
    }
}
