using Aperture.Debugging;
using Aperture.Dumps;
using Aperture.Memory;
using Aperture.Models;
using Aperture.Symbols;

var value = 0xFEDCBA9876543210UL;
if (BigEndianCodec.Decode<ulong>(BigEndianCodec.Encode(value)) != value) throw new InvalidDataException("Codec smoke check failed.");
using var database = new ExecutableDatabase();
var guid = Guid.NewGuid();
database.AddExecutable(new(guid, "game.xex", "game.pe", "", "", "game.pe", 1, 2, false));
if (database.FindExecutableByGuid(guid)?.SizeOfImage != 2) throw new InvalidDataException("Database smoke check failed.");
var path = Path.Combine(Path.GetTempPath(), "aperture-smoke-" + Guid.NewGuid().ToString("N") + ".dmp");
try
{
    DumpWriter.Write(path, Array.Empty<ModuleInfo>(), Array.Empty<DumpThreadSnapshot>(), [new(0x82000000, BigEndianCodec.Encode(value))]);
    using var dump = new DumpReader(path);
    if (BigEndianCodec.Decode<ulong>(dump.ReadMemory(0x82000000, 8)) != value) throw new InvalidDataException("Dump smoke check failed.");
}
finally { File.Delete(path); }
var unexpected = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name!).Where(n => !n.StartsWith("System", StringComparison.Ordinal) && !n.StartsWith("Microsoft.", StringComparison.Ordinal) && n is not ("Aperture" or "SmokeCheck" or "netstandard")).ToArray();
if (unexpected.Length > 0) throw new InvalidDataException("Unexpected assemblies: " + string.Join(", ", unexpected));
Console.WriteLine("PASS: isolated Aperture codec, executable database and dump round-trip; no JRPC, XDevkit or SDK assemblies loaded.");
