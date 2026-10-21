using Aperture.Debugging;
using Aperture.Dumps;
using Aperture.Models;
using Xunit;
namespace Aperture.Tests;
public sealed class DumpFlagsTests
{
    [Fact]
    public void ModuleFullNameSurvivesDumpSerialization()
    {
        var path = DumpTests.Temporary();
        try
        {
            DumpWriter.Write(path, [new("game.xex",0x82000000,4,0,0,0,0,4) {FullName="Hdd:\\game\\game.xex"}], [], []);
            using var dump = new DumpReader(path);
            Assert.Equal("Hdd:\\game\\game.xex", dump.Modules[0].FullName);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ExceptionStreamPreservesStopReasonAndParameters()
    {
        var path = DumpTests.Temporary();
        try
        {
            var e = DebugNotification.Parse("exception thread=9 code=0xC0000005 addr=0x82001234 noncont stop write=0x1234").EventInfo;
            DumpWriter.WriteWithFlags(path, [], [new(9,0,0,0,[])], [], 0, e);
            using var dump = new DumpReader(path);
            var restored = dump.GetStopEventInfo(9);
            Assert.Equal(e.Code, restored.Code);
            Assert.Equal(e.Address, restored.Address);
            Assert.Equal(e.Flags, restored.Flags);
            Assert.Equal(e.Parameters, restored.Parameters);
            Assert.True(restored.IsThreadStopped);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ContextRecordsRetainNativeXenonFrameSize()
    {
        var path = DumpTests.Temporary();
        try
        {
            var context = new byte[24];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(context, 1);
            DumpWriter.Write(path, [], [new(1,0,0,0,context)], []);
            using var dump = new DumpReader(path);
            Assert.Equal(2624u, dump.GetThreadInfo(1).ContextLength);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void FilteringKeepsModulePointersAndClearsOtherAlignedWords()
    {
        var path = DumpTests.Temporary();
        try
        {
            DumpWriter.WriteWithFlags(path, [new("game.xex", 0x82000000, 0x1000, 0, 0, 0, 0, 0x1000)], [], [new(0x90000000, [0x10,0,0,0x82, 1,2,3,4, 7])], DumpFlags.FilterMemory);
            using var dump = new DumpReader(path);
            Assert.Equal(new byte[] {0x10,0,0,0x82,0,0,0,0,7}, dump.ReadMemory(0x90000000, 9));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void FullMemoryWritesMemory64AndNormalizesRedundantHints()
    {
        var path = DumpTests.Temporary();
        try
        {
            DumpWriter.WriteWithFlags(path, [], [], [new(0x82000000, [1,2,3,4])], DumpFlags.WithFullMemory | DumpFlags.WithDataSegs | DumpFlags.FilterMemory | DumpFlags.ScanMemory | DumpFlags.WithIndirectlyReferencedMemory | DumpFlags.WithProcessThreadData | DumpFlags.WithPrivateReadWriteMemory);
            using var dump = new DumpReader(path);
            Assert.Equal(2UL, dump.Flags);
            Assert.Equal((ushort)3, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(dump.ReadStream(7)));
            Assert.Equal(24, dump.ReadStream(15).Length);
            Assert.Contains(dump.Streams, s => s.Type == 9);
            Assert.DoesNotContain(dump.Streams, s => s.Type == 5);
            Assert.Equal(new byte[] {1,2,3,4}, dump.ReadMemory(0x82000000, 4));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void FilterModulePathsRemovesDirectoryComponents()
    {
        var path = DumpTests.Temporary();
        try
        {
            DumpWriter.WriteWithFlags(path, [new("x:\\private\\game.xex",0x82000000,4,0,0,0,0,4)], [], [], DumpFlags.FilterModulePaths);
            using var dump = new DumpReader(path);
            Assert.Equal("game.xex", Assert.Single(dump.Modules).Name);
            Assert.Equal(128UL, dump.Flags);
        }
        finally { File.Delete(path); }
    }
}
