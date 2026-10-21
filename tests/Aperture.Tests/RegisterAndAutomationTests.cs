using System.Numerics;
using Aperture.Models;
using Aperture.Transport;
using Xunit;

namespace Aperture.Tests;

public sealed class RegisterAndAutomationTests
{
    [Fact]
    public void ContextPreservesVectorAndFloatingPointBits()
    {
        var type = typeof(ConsoleConnection).Assembly.GetType("Aperture.Debugging.RegisterSet"); Assert.NotNull(type);
        var context = type!.GetMethod("Parse")!.Invoke(null, [new string[] { "Iar=0x82000000 Gpr1=0q000000007A010000 Fpr0=0q3FF0000000000000 Vr0=0x3f800000,0x40000000,0x40400000,0x40800000" }])!;
        Assert.Equal(0x82000000u, type.GetMethod("Get32")!.Invoke(context, [Register32.iar]));
        Assert.Equal(0x7A010000UL, type.GetMethod("Get64")!.Invoke(context, [Register64.r1]));
        Assert.Equal(1d, type.GetMethod("GetDouble")!.Invoke(context, [RegisterDouble.fp0]));
        Assert.Equal(new Vector4(1, 2, 3, 4), type.GetMethod("GetVector")!.Invoke(context, [RegisterVector.v0]));
    }

    [Fact]
    public void GamepadWireMatchesNative12ByteLittleEndianPacket()
    {
        var type = typeof(ConsoleConnection).Assembly.GetType("Aperture.Automation.GamepadState"); Assert.NotNull(type);
        var state = Activator.CreateInstance(type!, [AutomationButtonFlags.A_Button, (byte)1, (byte)255, (short)-1, (short)32767, (short)-32768, (short)0])!;
        Assert.Equal(new byte[] { 0, 16, 1, 255, 255, 255, 255, 127, 0, 128, 0, 0 }, type!.GetMethod("ToWire")!.Invoke(state, null));
    }
}
