using Xunit;
namespace Aperture.Tests;
public sealed class PowerPcVmx128Tests
{
 [Theory]
 [InlineData(0x100000c3u,true)] [InlineData(0x100002c3u,true)]
 [InlineData(0x100001c3u,false)] [InlineData(0x100003c3u,false)]
 public void DecodesEveryExtendedVectorRegister(uint opcode,bool load)
 {
  var type=typeof(Aperture.Debugging.PowerPcUnwinder).Assembly.GetType("Aperture.Debugging.PowerPcVmx128");Assert.NotNull(type);var method=type.GetMethod("TryDecode");Assert.NotNull(method);
  for(var register=0;register<128;register++)for(var ra=0;ra<32;ra++)
  {
   var rb=31-ra;var instruction=opcode|((uint)(register&31)<<21)|((uint)ra<<16)|((uint)rb<<11)|((uint)(register>>5)<<2);
   object?[] args=[instruction,0,0,0,false];Assert.True((bool)method.Invoke(null,args)!);Assert.Equal(register,args[1]);Assert.Equal(ra,args[2]);Assert.Equal(rb,args[3]);Assert.Equal(load,args[4]);
  }
 }
 [Theory]
 [InlineData(0x7c0000ceu,true)] [InlineData(0x7c0002ceu,true)]
 [InlineData(0x7c0001ceu,false)] [InlineData(0x7c0003ceu,false)]
 public void DecodesStandardVectorTransfers(uint opcode,bool load)
 {
  var type=typeof(Aperture.Debugging.PowerPcUnwinder).Assembly.GetType("Aperture.Debugging.PowerPcVmx128");Assert.NotNull(type);var method=type.GetMethod("TryDecode");Assert.NotNull(method);
  object?[] args=[opcode|(31u<<21)|(1u<<16)|(12u<<11),0,0,0,false];Assert.True((bool)method.Invoke(null,args)!);Assert.Equal(31,args[1]);Assert.Equal(1,args[2]);Assert.Equal(12,args[3]);Assert.Equal(load,args[4]);
 }
 [Theory]
 [InlineData(0x10000000u)] [InlineData(0x10000083u)] [InlineData(0x100001c2u)] [InlineData(0x7c0001cfu)] [InlineData(0xffffffffu)]
 public void RejectsOtherVectorAndMalformedInstructions(uint word)
 {
  var type=typeof(Aperture.Debugging.PowerPcUnwinder).Assembly.GetType("Aperture.Debugging.PowerPcVmx128");Assert.NotNull(type);var method=type.GetMethod("TryDecode");Assert.NotNull(method);object?[] args=[word,0,0,0,false];Assert.False((bool)method.Invoke(null,args)!);
 }
}
