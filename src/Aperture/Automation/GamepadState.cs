using System.Buffers.Binary;
using Aperture.Models;

namespace Aperture.Automation;

public sealed record GamepadState(AutomationButtonFlags Buttons, byte LeftTrigger, byte RightTrigger, short LeftThumbX, short LeftThumbY, short RightThumbX, short RightThumbY)
{
    public byte[] ToWire()
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)Buttons)); bytes[2] = LeftTrigger; bytes[3] = RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), LeftThumbX); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), LeftThumbY);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), RightThumbX); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), RightThumbY);
        return bytes;
    }
}
