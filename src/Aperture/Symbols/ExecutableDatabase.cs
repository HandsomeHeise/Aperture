using System.Text;
using System.Xml;
using System.Xml.Linq;
using Aperture.Transport;
using Aperture.Models;

namespace Aperture.Symbols;

public sealed record ExecutableInfo
{
    private string xboxPath, pePath, pdbPath, publicPdbPath;
    internal string AbsoluteXboxPath => Expand(xboxPath);
    internal string AbsolutePePath => Expand(pePath);
    internal string AbsoluteSymbolPath => Expand(pdbPath);
    internal string AbsolutePublicSymbolPath => Expand(publicPdbPath);
    internal string StoredXboxPath => xboxPath;
    internal string StoredPePath => pePath;
    internal string StoredSymbolPath => pdbPath;
    internal string StoredPublicSymbolPath => publicPdbPath;
    public Guid SymbolGuid { get; }
    public string XboxExecutablePath => Project(xboxPath);
    public string PortableExecutablePath => Project(pePath);
    public string SymbolPath => Project(pdbPath);
    public string PublicSymbolPath => Project(publicPdbPath);
    public string ModuleName { get; }
    public uint TimeDateStamp { get; }
    public uint SizeOfImage { get; }
    public bool StoreRelativePath { get; }
    private string basePath = Environment.CurrentDirectory;
    public string BasePath { get => basePath; set => basePath = value ?? ""; }
    public bool PropGetRelativePath { get; set; }
    public ExecutableInfo(Guid symbolGuid, string xboxExecutablePath, string portableExecutablePath, string symbolPath, string publicSymbolPath, string moduleName, uint timeDateStamp, uint sizeOfImage, bool storeRelativePath)
        : this(symbolGuid, xboxExecutablePath, portableExecutablePath, symbolPath, publicSymbolPath, moduleName, timeDateStamp, sizeOfImage, storeRelativePath, false) { }
    internal ExecutableInfo(Guid symbolGuid, string xboxExecutablePath, string portableExecutablePath, string symbolPath, string publicSymbolPath, string moduleName, uint timeDateStamp, uint sizeOfImage, bool storeRelativePath, bool pathsAlreadyStored)
    {
        SymbolGuid = symbolGuid; xboxPath = xboxExecutablePath; pePath = portableExecutablePath; pdbPath = symbolPath; publicPdbPath = publicSymbolPath;
        ModuleName = moduleName; TimeDateStamp = timeDateStamp; SizeOfImage = sizeOfImage; StoreRelativePath = storeRelativePath;
        basePath = Path.IsPathFullyQualified(xboxExecutablePath) ? Path.GetDirectoryName(xboxExecutablePath)! : Environment.CurrentDirectory;
        string Stored(string path) => !pathsAlreadyStored && storeRelativePath && Path.IsPathFullyQualified(path) ? Path.GetRelativePath(basePath, path) : path;
        xboxPath = Stored(xboxPath); pePath = Stored(pePath); pdbPath = Stored(pdbPath); publicPdbPath = Stored(publicPdbPath);
    }
    // Native xbdm RVA 0x24A70: relative entries concatenate BasePath only when
    // PropGetRelativePath is false. RVA 0x248C0 stores BasePath verbatim.
    private string Project(string path) => PropGetRelativePath ? path : Expand(path);
    private string Expand(string path) => StoreRelativePath && path.Length > 0 ? BasePath.TrimEnd('\\') + "\\" + path : path;
}

public sealed class ExecutableDatabase : IDisposable
{
    private Dictionary<Guid, ExecutableInfo> entries = [];
    private bool readOnly;
    private FileStream? reservation;
    public bool IsDirty { get; private set; }
    public string? CurrentFileName { get; private set; }
    public IReadOnlyList<ExecutableInfo> Executables => entries.Values.OrderBy(e => e.SymbolGuid).ToArray();
    public void LoadDatabase(string path, bool readOnly = false)
    {
        reservation?.Dispose(); reservation = null;
        LoadCore(path, readOnly, null);
    }
    public void LoadDatabase(string path, bool readOnly, CreateDisposition createDisposition, ShareMode shareMode)
    {
        var mode = createDisposition switch { CreateDisposition.CreateNew => FileMode.CreateNew, CreateDisposition.CreateAlways => FileMode.Create, CreateDisposition.OpenExisting => FileMode.Open, CreateDisposition.OpenAlways => FileMode.OpenOrCreate, _ => throw new ArgumentOutOfRangeException(nameof(createDisposition)) };
        if (((int)shareMode & ~7) != 0) throw new ArgumentOutOfRangeException(nameof(shareMode));
        if (readOnly && mode != FileMode.Open) throw new ArgumentException("Read-only databases must be opened existing.", nameof(createDisposition));
        reservation?.Dispose(); reservation = null;
        var stream = new FileStream(Path.GetFullPath(path), mode, readOnly ? FileAccess.Read : FileAccess.ReadWrite, (FileShare)(int)shareMode);
        try
        {
            if (stream.Length == 0 && mode != FileMode.Open)
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.Write("<XboxExecutableDatabase Version=\"1.0\"/>"); writer.Flush(); stream.Position = 0;
            }
            LoadCore(path, readOnly, stream);
            reservation = stream;
        }
        catch { stream.Dispose(); throw; }
    }
    private void LoadCore(string path, bool readOnly, FileStream? stream)
    {
        var fullPath = Path.GetFullPath(path); var directory = Path.GetDirectoryName(fullPath)!;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024, CloseInput = false };
        using var reader = stream is null ? XmlReader.Create(fullPath, settings) : XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "XboxExecutableDatabase" || (string?)document.Root.Attribute("Version") != "1.0") throw new InvalidDataException("Unsupported executable database schema.");
        var loaded = new Dictionary<Guid, ExecutableInfo>();
        foreach (var element in document.Root.Elements("Executable"))
        {
            var guid = Guid.Parse(Required(element, "PDBGUID"));
            var relativeText = (string?)element.Attribute("RELPATHS") ?? "";
            var relative = relativeText.Length > 0 && relativeText[0] is '1' or 'Y' or 'y' or 'T' or 't';
            var entry = new ExecutableInfo(guid, (string?)element.Attribute("XEX") ?? "", (string?)element.Attribute("PE") ?? "", (string?)element.Attribute("PDB") ?? "", (string?)element.Attribute("PUBPDB") ?? "", (string?)element.Attribute("MODULE") ?? "", ProtocolFields.UInt32((string?)element.Attribute("TIMESTAMP") ?? "0"), ProtocolFields.UInt32((string?)element.Attribute("SIZE") ?? "0"), relative, true);
            entry.BasePath = directory;
            if (!loaded.TryAdd(guid, entry)) throw new InvalidDataException("Duplicate executable GUID in database.");
        }
        reservation?.Dispose(); reservation = null;
        entries = loaded; this.readOnly = readOnly; CurrentFileName = fullPath; IsDirty = false;
    }
    public void SaveDatabase(string path, bool remember = true)
    {
        var fullPath = Path.GetFullPath(path);
        if (readOnly && string.Equals(fullPath, CurrentFileName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The database was opened read-only.");
        var directory = Path.GetDirectoryName(fullPath)!;
        var root = new XElement("XboxExecutableDatabase", new XAttribute("Version", "1.0"));
        foreach (var entry in Executables)
        {
            root.Add(new XElement("Executable", new XAttribute("PDBGUID", entry.SymbolGuid.ToString("D")), new XAttribute("XEX", entry.StoredXboxPath), new XAttribute("PE", entry.StoredPePath), new XAttribute("PDB", entry.StoredSymbolPath), new XAttribute("PUBPDB", entry.StoredPublicSymbolPath), new XAttribute("MODULE", entry.ModuleName), new XAttribute("TIMESTAMP", FormattableString.Invariant($"0x{entry.TimeDateStamp:X8}")), new XAttribute("SIZE", FormattableString.Invariant($"0x{entry.SizeOfImage:X8}")), new XAttribute("RELPATHS", entry.StoreRelativePath ? "Yes" : "No")));
        }
        var temporary = Path.Combine(directory, ".aperture-db-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) })) new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
            if (reservation is not null && string.Equals(fullPath, CurrentFileName, StringComparison.OrdinalIgnoreCase))
            {
                using var source = File.OpenRead(temporary);
                reservation.Position = 0; source.CopyTo(reservation); reservation.SetLength(reservation.Position); reservation.Flush(true);
            }
            else File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        if (remember)
        {
            if (!string.Equals(fullPath, CurrentFileName, StringComparison.OrdinalIgnoreCase)) { reservation?.Dispose(); reservation = null; readOnly = false; }
            CurrentFileName = fullPath;
            foreach (var entry in entries.Values) entry.BasePath = directory;
        }
        IsDirty = false;
    }
    public void AddExecutable(ExecutableInfo executable)
    {
        Writable(); ArgumentNullException.ThrowIfNull(executable);
        entries[executable.SymbolGuid] = executable; IsDirty = true;
    }
    public void AddExecutable(string xboxExecutablePath, string? portableExecutablePath = null, string? symbolPath = null, string? publicSymbolPath = null, bool explicitFilesOnly = false, bool storeRelativePath = false)
    {
        Writable(); ArgumentException.ThrowIfNullOrWhiteSpace(xboxExecutablePath);
        if (!File.Exists(xboxExecutablePath)) throw new FileNotFoundException("Xbox executable does not exist.", xboxExecutablePath);
        var pe = portableExecutablePath ?? (explicitFilesOnly ? throw new ArgumentException("An explicit portable executable path is required.") : Path.ChangeExtension(xboxExecutablePath, ".pe"));
        var info = PeMetadata.Read(pe);
        var pdb = symbolPath ?? (explicitFilesOnly ? "" : Path.ChangeExtension(pe, ".pdb"));
        if (pdb.Length > 0 && !File.Exists(pdb))
        {
            if (symbolPath is not null || explicitFilesOnly) throw new FileNotFoundException("Symbol file does not exist.", pdb);
            pdb = "";
        }
        var guid = info.PdbGuid ?? (pdb.Length > 0 ? PdbIdentity.Read(pdb).Guid : throw new InvalidDataException("Executable has no CodeView GUID and no symbol file was supplied."));
        if (pdb.Length > 0)
        {
            var identity = PdbIdentity.Read(pdb);
            if (identity.Guid != guid || info.PdbGuid is not null && identity.Age != info.PdbAge) throw new InvalidDataException("Executable and PDB identities differ.");
        }
        if (!string.IsNullOrEmpty(publicSymbolPath) && !File.Exists(publicSymbolPath)) throw new FileNotFoundException("Public symbol file does not exist.", publicSymbolPath);
        AddExecutable(new(guid, Path.GetFullPath(xboxExecutablePath), Path.GetFullPath(pe), pdb.Length == 0 ? "" : Path.GetFullPath(pdb), string.IsNullOrEmpty(publicSymbolPath) ? "" : Path.GetFullPath(publicSymbolPath), Path.GetFileName(pe), info.TimeDateStamp, info.SizeOfImage, storeRelativePath));
    }
    public ExecutableInfo? FindExecutableByGuid(Guid guid) => entries.GetValueOrDefault(guid);
    public ExecutableInfo? FindExecutableForSymServ(string module, uint timestamp, uint size) => entries.Values.FirstOrDefault(e => string.Equals(e.ModuleName, module, StringComparison.OrdinalIgnoreCase) && e.TimeDateStamp == timestamp && e.SizeOfImage == size);
    public void RemoveExecutable(Guid guid) { Writable(); if (entries.Remove(guid)) IsDirty = true; }
    public void ResetDatabase() { Writable(); if (entries.Count != 0) { entries.Clear(); IsDirty = true; } }
    public void CloseDatabase() { reservation?.Dispose(); reservation = null; }
    public void Dispose() { CloseDatabase(); entries.Clear(); CurrentFileName = null; IsDirty = false; readOnly = false; }
    private void Writable() { if (readOnly) throw new InvalidOperationException("The database was opened read-only."); }
    private static string Required(XElement element, string name) => (string?)element.Attribute(name) ?? throw new InvalidDataException("Missing database attribute " + name);
}
