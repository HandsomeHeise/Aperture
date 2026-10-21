namespace Aperture.Debugging;

/// <summary>Decodes full-width indexed VMX and Xbox 360 VMX128 vector transfers.</summary>
public static class PowerPcVmx128
{
    /// <summary>Recognizes lvx/lvxl/stvx/stvxl and their VMX128 counterparts. Address operands are GPR indices; rA=0 means zero, and the effective address is aligned down to 16 bytes.</summary>
    public static bool TryDecode(uint instruction, out int register, out int ra, out int rb, out bool isLoad)
    {
        // Opcode facts and VX128_1 fields verified against xenia-project/xenia:
        // src/xenia/cpu/ppc/ppc_opcode_table_gen.cc and ppc_instr.h.
        var extended = instruction >> 26 == 4;
        var opcode = instruction & (extended ? 0xfc0007f3u : 0xfc0007ffu);
        isLoad = opcode is 0x100000c3 or 0x100002c3 or 0x7c0000ce or 0x7c0002ce;
        if (!isLoad && opcode is not (0x100001c3 or 0x100003c3 or 0x7c0001ce or 0x7c0003ce))
        {
            register = ra = rb = 0;
            return false;
        }
        register = (int)(instruction >> 21 & 31);
        if (extended) register |= (int)(instruction >> 2 & 3) << 5;
        ra = (int)(instruction >> 16 & 31);
        rb = (int)(instruction >> 11 & 31);
        return true;
    }
}
