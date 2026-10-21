using Aperture.Symbols;
using Aperture.Models;
using Xunit;

namespace Aperture.Tests;

public sealed class ExecutableDatabaseParityTests
{
    private static ExecutableInfo Entry(string directory) => new(Guid.Parse("12345678-1234-5678-9abc-def012345678"), Path.Combine(directory, "game.xex"), Path.Combine(directory, "game.pe"), Path.Combine(directory, "game.pdb"), "", "game.pe", 12, 4096, true);

    [Fact]
    public void RelativeProjectionUsesBaseDirectoryWithoutChangingSavedIdentity()
    {
        var directory = Path.GetTempPath();
        var entry = Entry(directory);
        var basePath = typeof(ExecutableInfo).GetProperty("BasePath");
        Assert.NotNull(basePath);
        basePath.SetValue(entry, directory);
        var relative = typeof(ExecutableInfo).GetProperty("PropGetRelativePath");
        Assert.NotNull(relative);
        relative.SetValue(entry, true);
        Assert.Equal("game.xex", entry.XboxExecutablePath);
        Assert.Equal("game.pdb", entry.SymbolPath);
        Assert.Equal("", entry.PublicSymbolPath);
        relative.SetValue(entry, false);
        Assert.Equal(Path.Combine(directory, "game.xex"), entry.XboxExecutablePath);
    }

    [Fact]
    public void ChangingBasePathRebasesRelativeEntriesAndLeavesAbsoluteEntriesAlone()
    {
        var entry = Entry(Path.GetTempPath());
        entry.BasePath = @"D:\symbols";
        Assert.Equal(@"D:\symbols\game.xex", entry.XboxExecutablePath);
        entry.PropGetRelativePath = true;
        Assert.Equal("game.xex", entry.XboxExecutablePath);
        var absolute = new ExecutableInfo(Guid.NewGuid(), @"C:\game.xex", @"C:\game.pe", "", "", "game.pe", 1, 2, false);
        absolute.BasePath = @"D:\symbols"; absolute.PropGetRelativePath = true;
        Assert.Equal(@"C:\game.xex", absolute.XboxExecutablePath);
    }

    [Fact]
    public void SavingCopyClearsDirtyWithoutChangingRememberedFilename()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var db = new ExecutableDatabase();
            var original = Path.Combine(directory, "original.xml");
            db.SaveDatabase(original);
            db.AddExecutable(Entry(directory));
            db.SaveDatabase(Path.Combine(directory, "copy.xml"), false);
            Assert.Equal(original, db.CurrentFileName);
            Assert.False(db.IsDirty);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RememberedSaveRebasesStoredRelativePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "moved"));
        try
        {
            using var db = new ExecutableDatabase(); var entry = Entry(directory); db.AddExecutable(entry);
            entry.PropGetRelativePath = true;
            db.SaveDatabase(Path.Combine(directory, "moved", "database.xml"));
            Assert.Equal("game.xex", entry.XboxExecutablePath);
            entry.PropGetRelativePath = false;
            Assert.Equal(Path.Combine(directory, "moved", "game.xex"), entry.XboxExecutablePath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DatabaseCanCreateAndKeepsRequestedShareReservationUntilClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid() + ".xml");
        try
        {
            var overload = typeof(ExecutableDatabase).GetMethod("LoadDatabase", [typeof(string), typeof(bool), typeof(CreateDisposition), typeof(ShareMode)]);
            Assert.NotNull(overload);
            using var db = new ExecutableDatabase();
            overload.Invoke(db, [path, false, CreateDisposition.CreateNew, ShareMode.ShareNone]);
            Assert.Empty(db.Executables);
            Assert.Throws<IOException>(() => File.OpenRead(path));
            db.AddExecutable(Entry(Path.GetTempPath()));
            db.SaveDatabase(path);
            Assert.False(db.IsDirty);
            db.CloseDatabase();
            using var other = new ExecutableDatabase(); other.LoadDatabase(path);
            Assert.Single(other.Executables);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NativeXmlAcceptsBooleanPrefixesAndAbsentOptionalAttributes()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid() + ".xml");
        try
        {
            File.WriteAllText(path, "<XboxExecutableDatabase Version=\"1.0\"><Executable PDBGUID=\"12345678-1234-5678-9abc-def012345678\" XEX=\"game.xex\" RELPATHS=\"True\"/></XboxExecutableDatabase>");
            using var db = new ExecutableDatabase(); db.LoadDatabase(path);
            var entry = Assert.Single(db.Executables);
            Assert.True(entry.StoreRelativePath);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "game.xex"), entry.XboxExecutablePath);
            Assert.Equal("", entry.PortableExecutablePath);
            Assert.Equal("", entry.ModuleName);
            Assert.Equal(0u, entry.TimeDateStamp); Assert.Equal(0u, entry.SizeOfImage);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EmptyResetDoesNotMakeCleanDatabaseDirty()
    {
        using var db = new ExecutableDatabase(); db.ResetDatabase(); Assert.False(db.IsDirty);
    }

    [Fact]
    public void ReloadingSameReservedFileReleasesPreviousHandleBeforeOpening()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid() + ".xml");
        try
        {
            File.WriteAllText(path, "<XboxExecutableDatabase Version=\"1.0\"/>");
            using var db = new ExecutableDatabase();
            db.LoadDatabase(path, false, CreateDisposition.OpenExisting, ShareMode.ShareNone);
            db.LoadDatabase(path, false, CreateDisposition.OpenExisting, ShareMode.ShareNone);
            Assert.Empty(db.Executables);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XmlRelativeProjectionPreservesStoredPathText()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid() + ".xml");
        try
        {
            File.WriteAllText(path, "<XboxExecutableDatabase Version=\"1.0\"><Executable PDBGUID=\"12345678-1234-5678-9abc-def012345678\" XEX=\"sub\\..\\game.xex\" RELPATHS=\"Yes\"/></XboxExecutableDatabase>");
            using var db = new ExecutableDatabase(); db.LoadDatabase(path);
            var entry = Assert.Single(db.Executables); entry.PropGetRelativePath = true;
            Assert.Equal(@"sub\..\game.xex", entry.XboxExecutablePath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ClosingReleasesFileWhileKeepingLoadedEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-parity-" + Guid.NewGuid() + ".xml");
        try
        {
            using var db = new ExecutableDatabase(); db.SaveDatabase(path); db.AddExecutable(Entry(Path.GetTempPath()));
            db.CloseDatabase();
            Assert.Single(db.Executables); Assert.True(db.IsDirty); Assert.Equal(path, db.CurrentFileName);
        }
        finally { File.Delete(path); }
    }
}
