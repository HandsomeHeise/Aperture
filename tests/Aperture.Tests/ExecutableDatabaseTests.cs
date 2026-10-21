using Aperture.Symbols;
using Xunit;

namespace Aperture.Tests;

public sealed class ExecutableDatabaseTests
{
    [Fact]
    public void ReadsNativeXmlAndResolvesRelativePathsWithoutCom()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aperture-db-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "database.xml");
        try
        {
            File.WriteAllText(path, "<XboxExecutableDatabase Version=\"1.0\"><Executable PDBGUID=\"12345678-1234-5678-9abc-def012345678\" XEX=\"game.xex\" PDB=\"game.pdb\" PUBPDB=\"\" MODULE=\"game.pe\" TIMESTAMP=\"0x12345678\" SIZE=\"0x00010000\" RELPATHS=\"Yes\"/></XboxExecutableDatabase>");
            using var db = new ExecutableDatabase(); db.LoadDatabase(path);
            var entry = db.FindExecutableByGuid(Guid.Parse("12345678-1234-5678-9abc-def012345678")); Assert.NotNull(entry);
            Assert.Equal(Path.Combine(directory, "game.xex"), entry!.XboxExecutablePath);
            Assert.Same(entry, db.FindExecutableForSymServ("GAME.PE", 0x12345678, 0x10000));
            Assert.False(db.IsDirty); Assert.Equal(Path.GetFullPath(path), db.CurrentFileName);
            db.RemoveExecutable(entry.SymbolGuid); Assert.True(db.IsDirty); Assert.Empty(db.Executables);
            var saved = Path.Combine(directory, "saved.xml"); db.SaveDatabase(saved); Assert.False(db.IsDirty);
            using var reloaded = new ExecutableDatabase(); reloaded.LoadDatabase(saved); Assert.Empty(reloaded.Executables);
        }
        finally { File.Delete(path); File.Delete(Path.Combine(directory, "saved.xml")); Directory.Delete(directory); }
    }

    [Fact]
    public void ReadOnlyDatabaseRefusesMutationsAndExternalEntities()
    {
        var path = Path.Combine(Path.GetTempPath(), "aperture-db-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(path, "<XboxExecutableDatabase Version=\"1.0\"/>");
            using var db = new ExecutableDatabase(); db.LoadDatabase(path, true);
            Assert.Throws<InvalidOperationException>(() => db.RemoveExecutable(Guid.NewGuid()));
            File.WriteAllText(path, "<!DOCTYPE XboxExecutableDatabase [<!ENTITY e SYSTEM 'file:///missing'>]><XboxExecutableDatabase Version=\"1.0\">&e;</XboxExecutableDatabase>");
            Assert.ThrowsAny<Exception>(() => db.LoadDatabase(path));
        }
        finally { File.Delete(path); }
    }
}
