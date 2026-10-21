using Aperture.Debugging;
using Aperture.Dumps;
using Aperture.Transport;
using Aperture.Tests.Fixtures;
using Xunit;

namespace Aperture.Tests;
public sealed class DumpCaptureTests
{
    [Fact]
    public async Task ModuleIdentityKeepsFullPathAndNativeUnavailableDebugDirectories()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- modules\r\nname=\"game.xex\" base=0x82000000 size=4 check=1 timestamp=2\r\n.\r\n", ct);
            Assert.Equal("xbeinfo name=\"game.xex\"", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- info\r\nname=\"Hdd:\\game\\game.xex\"\r\n.\r\n", ct);
            Assert.Equal("threads", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- threads\r\n.\r\n", ct);
        });
        var path = DumpTests.Temporary();
        try
        {
            await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
            await new DumpCapture(new DebugTarget(connection)).WriteAsync(path, Aperture.Models.DumpFlags.ScanMemory);
            using var dump = new DumpReader(path);
            Assert.Equal("Hdd:\\game\\game.xex", Assert.Single(dump.Modules).FullName);
            var descriptors = dump.ReadStream(4);
            Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptors.AsSpan(80)));
            Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptors.AsSpan(88)));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task IndirectMemoryUsesNativePointerWindowAndKeepsThreadStackDescriptor()
    {
        var registers = "Msr=0 Iar=0x82000000 Lr=0 Ctr=0 Cr=0 Xer=0 " + string.Join(' ', Enumerable.Range(0,32).Select(i => $"Gpr{i}=0q{(i == 1 ? 0x90000000UL : 0):X16}"));
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- modules\r\n.\r\n", ct);
            Assert.Equal("threads", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- threads\r\n1\r\n.\r\n", ct);
            Assert.Equal("threadinfo thread=0x00000001", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- info\r\nbase=0x90000004 limit=0x90000000\r\n.\r\n", ct);
            Assert.Equal("getcontext thread=0x00000001 control int fp vr", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- context\r\n"+registers+"\r\n.\r\n", ct);
            Assert.Equal("isstopped thread=0x00000001", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "408- not stopped\r\n", ct);
            Assert.Equal("getmem addr=0x90000000 length=0x4", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- memory\r\n00010082\r\n.\r\n", ct);
            Assert.Equal("walkmem", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- memory\r\nbase=0x82000000 size=0x400 protect=4\r\n.\r\n", ct);
            Assert.Equal("getmem addr=0x82000000 length=0x400", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- memory\r\n"+new string('A',2048)+"\r\n.\r\n", ct);
        });
        var path = DumpTests.Temporary();
        try
        {
            await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
            await new DumpCapture(new DebugTarget(connection)).WriteAsync(path, Aperture.Models.DumpFlags.WithIndirectlyReferencedMemory);
            using var dump = new DumpReader(path);
            Assert.Equal(0x90000000u, dump.GetThreadInfo(1).StackAddress);
            Assert.Equal(4u, dump.GetThreadInfo(1).StackLength);
            Assert.Equal(new byte[] {0xAA,0xAA,0xAA,0xAA}, dump.ReadMemory(0x82000000,4));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task FullMemoryNormalizesHintsAndCapturesOnlyReadableCommittedRegions()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("walkmem", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- memory\r\nbase=0x82000000 size=4 protect=4\r\nbase=0x83000000 size=4 protect=0x200\r\n.\r\n", ct);
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- modules\r\n.\r\n", ct);
            Assert.Equal("threads", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- threads\r\n.\r\n", ct);
            Assert.Equal("getmem addr=0x82000000 length=0x4", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- memory\r\n01020304\r\n.\r\n", ct);
        });
        var path = DumpTests.Temporary();
        try
        {
            await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
            await new DumpCapture(new DebugTarget(connection)).WriteAsync(path, Aperture.Models.DumpFlags.WithFullMemory | Aperture.Models.DumpFlags.FilterMemory | Aperture.Models.DumpFlags.WithProcessThreadData);
            using var dump = new DumpReader(path);
            Assert.Equal(2UL, dump.Flags);
            Assert.Single(dump.MemoryRegions);
            Assert.Equal(new byte[] {1,2,3,4}, dump.ReadMemory(0x82000000, 4));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task EmptyNativeMetadataHintsDoNotInventHistoryOrProcessStreams()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- modules\r\n.\r\n", ct);
            Assert.Equal("threads", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- threads\r\n.\r\n", ct);
        });
        var path = DumpTests.Temporary();
        try
        {
            await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
            var flags = Aperture.Models.DumpFlags.WithUnloadedModules | Aperture.Models.DumpFlags.WithProcessThreadData | Aperture.Models.DumpFlags.ScanMemory | Aperture.Models.DumpFlags.FilterModulePaths;
            await new DumpCapture(new DebugTarget(connection)).WriteAsync(path, flags);
            using var dump = new DumpReader(path);
            Assert.Equal((ulong)flags, dump.Flags);
            Assert.DoesNotContain(dump.Streams, stream => stream.Type is 14 or 12);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task SelectedMemoryCaptureProducesAReadableIndependentDump()
    {
        await using var server = new DebugMonitorServer(async (s, ct) =>
        {
            Assert.Equal("modules", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- modules\r\n.\r\n", ct);
            Assert.Equal("threads", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- threads\r\n.\r\n", ct);
            Assert.Equal("getmem addr=0x82000000 length=0x4", await DebugMonitorServer.ReadLine(s, ct)); await DebugMonitorServer.Write(s, "202- data\r\n01020304\r\n.\r\n", ct);
        });
        var path = DumpTests.Temporary();
        try
        {
            await using var connection = await ConsoleConnection.ConnectAsync("127.0.0.1", server.Port);
            await new DumpCapture(new DebugTarget(connection)).WriteAsync(path, [new(0x82000000, 4)]);
            using var dump = new DumpReader(path);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, dump.ReadMemory(0x82000000, 4));
        }
        finally { File.Delete(path); }
    }
}
