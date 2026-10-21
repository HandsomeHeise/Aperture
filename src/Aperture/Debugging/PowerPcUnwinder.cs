using System.Buffers.Binary;
using System.Numerics;
using Aperture.Models;

namespace Aperture.Debugging;

public static class PowerPcUnwinder
{
    /// <summary>Restores a caller from an executed PPC prolog. Unknown save sequences fail explicitly.</summary>
    public static async Task<RegisterSet?> UnwindAsync(RegisterSet current, FunctionInfo? function, Func<uint, int, CancellationToken, Task<byte[]>> readMemory, CancellationToken cancellationToken = default, Func<uint, CancellationToken, Task<FunctionInfo?>>? lookupFunction = null)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(readMemory);
        if (function is null) throw new NotSupportedException("Unwinding requires runtime function metadata; a missing table does not prove a leaf function.");
        var pc = current.Get32(Register32.iar);
        var stack = checked((uint)current.Get64(Register64.r1));
        if ((stack & 15) != 0) throw new InvalidDataException("Unaligned Xenon stack pointer.");
        if (function.FunctionType == FunctionType.NoPData)
        {
            if (pc < function.BeginAddress || pc > function.FunctionEndAddress) throw new InvalidDataException("Leaf instruction pointer is outside its verified image.");
            var link = current.Get32(Register32.lr);
            if (link == 0) return null;
            if ((link & 3) != 0 || link == pc) throw new InvalidDataException("Invalid leaf caller address.");
            var leaf = current.Clone(); leaf.Set32(Register32.iar, link); return leaf.Clone();
        }
        var words = Array.Empty<uint>();
        var bodyWords = new List<uint>();
        if (function is not null)
        {
            if (pc < function.BeginAddress || pc > function.FunctionEndAddress || function.PrologEndAddress < function.BeginAddress || function.PrologEndAddress > function.FunctionEndAddress) throw new InvalidDataException("Instruction pointer is outside the function.");
            if (pc > function.PrologEndAddress || function.FunctionType == FunctionType.RestoreMillicode)
            {
                var epilog = await PowerPcEpilog.TryUnwindAsync(current, function, readMemory, cancellationToken, lookupFunction).ConfigureAwait(false);
                if (epilog is not null) return epilog.Caller;
                var bodyLength = pc - function.PrologEndAddress;
                if (bodyLength > 1024 * 1024 || (bodyLength & 3) != 0) throw new NotSupportedException("Function body is too large to exclude executed stack restoration.");
                var body = await readMemory(function.PrologEndAddress, (int)bodyLength, cancellationToken).ConfigureAwait(false);
                if (body.Length != bodyLength) throw new EndOfStreamException("Truncated function body.");
                for (var offset = 0; offset < body.Length; offset += 4)
                {
                    var operation = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(offset, 4));
                    bodyWords.Add(operation);
                    var opcode = operation >> 26; var target = (operation >> 21) & 31; var address = (operation >> 16) & 31;
                    // Conservatively inspect every preceding instruction, including branch paths:
                    // a possible restored SP invalidates the prolog's saved-address model.
                    if (opcode is 16 or 18 || operation == 0x60000000) continue;
                    var ordinaryStore = opcode is 36 or 38 or 44 or 47 or 52 or 54 || opcode == 62 && (operation & 3) == 0;
                    if (opcode == 4 || PowerPcVmx128.TryDecode(operation, out _, out _, out _, out _)) continue;
                    if (!ordinaryStore && (target == 1 || address == 1 && (opcode is >= 24 and <= 30 || opcode == 31 && ((operation >> 1) & 1023) is 24 or 28 or 60 or 124 or 284 or 316 or 412 or 444 or 476 or 536 or 792 or 824 or 922 or 954))) throw new NotSupportedException("Possible epilog or stack-pointer restoration requires unsupported control-flow unwinding.");
                }
            }
            var length = Math.Min(pc, function.PrologEndAddress) - function.BeginAddress;
            if (length > 4096 || (length & 3) != 0) throw new InvalidDataException("Invalid prolog extent.");
            var bytes = await readMemory(function.BeginAddress, (int)length, cancellationToken).ConfigureAwait(false);
            if (bytes.Length != length) throw new EndOfStreamException("Truncated function prolog.");
            words = Enumerable.Range(0, bytes.Length / 4).Select(i => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i * 4, 4))).ToArray();
            if (words.Any(w => w >> 26 == 18))
            {
                var expanded = new List<uint>();
                for (var i = 0; i < words.Length; i++)
                {
                    var instruction = words[i];
                    if (instruction >> 26 != 18) { expanded.Add(instruction); continue; }
                    if (lookupFunction is null) throw new NotSupportedException("Millicode requires runtime function lookup.");
                    var displacement = unchecked((int)(instruction & 0x03fffffc));
                    if ((displacement & 0x02000000) != 0) displacement |= unchecked((int)0xfc000000);
                    var target = (instruction & 2) != 0 ? unchecked((uint)displacement) : checked((uint)((long)function.BeginAddress + i * 4 + displacement));
                    var helper = await lookupFunction(target, cancellationToken).ConfigureAwait(false);
                    if (helper?.FunctionType != FunctionType.SaveMillicode || target < helper.BeginAddress || target >= helper.FunctionEndAddress || helper.PrologEndAddress <= target || helper.PrologEndAddress - target > 1024) throw new NotSupportedException("Prolog branch does not identify bounded save millicode.");
                    var code = await readMemory(target, (int)(helper.PrologEndAddress - target), cancellationToken).ConfigureAwait(false);
                    if (code.Length != helper.PrologEndAddress - target) throw new EndOfStreamException("Truncated save millicode.");
                    for (var o = 0; o + 4 <= code.Length; o += 4)
                    {
                        var operation = BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(o, 4));
                        if (operation == 0x4e800020) { break; }
                        if (operation >> 26 is 18 or 16) throw new NotSupportedException("Save millicode contains unsupported control flow.");
                        expanded.Add(operation);
                    }
                }
                words = expanded.ToArray();
            }
        }
        var allocations = new Dictionary<int, long?>();
        var constants = new Dictionary<uint, long>();
        for (var instructionIndex = 0; instructionIndex < words.Length; instructionIndex++)
        {
            var instruction = words[instructionIndex];
            var op = instruction >> 26; var rt = (instruction >> 21) & 31; var ra = (instruction >> 16) & 31;
            if (op is 14 or 15 && rt != 1)
            {
                if (ra == 0) constants[rt] = op == 15 ? (long)(short)instruction << 16 : (short)instruction;
                else if (constants.TryGetValue(ra, out var prior)) constants[rt] = prior + (op == 15 ? (long)(short)instruction << 16 : (short)instruction);
                else constants.Remove(rt);
            }
            if ((instruction & 0xfc1fffff) is 0x7c0802a6 or 0x7c000026) constants.Remove(rt);
            if (op == 31 && ((instruction >> 1) & 1023) == 444 && rt == ((instruction >> 11) & 31))
            {
                if (constants.TryGetValue(rt, out var copied)) constants[ra] = copied; else constants.Remove(ra);
            }
            if (op == 31 && ((instruction >> 1) & 1023) == 40 && rt == 1 && ((instruction >> 11) & 31) == 1)
            {
                if (!constants.TryGetValue(ra, out var amount)) throw new NotSupportedException("Stack allocation uses an unknown register.");
                allocations[instructionIndex] = -amount; continue;
            }
            if (rt == 1 && op is 14 or 15 && ra != 1 || rt == 1 && ((instruction & 0xfc1fffff) is 0x7c0802a6 or 0x7c000026) || ra == 1 && op == 31 && ((instruction >> 1) & 1023) == 444)
                throw new NotSupportedException("Stack-pointer assignment has no supported inverse.");
            if (rt == 1 && ra == 1 && op is 14 or 15 || ra == 1 && (op == 37 || op == 62 && (instruction & 3) == 1)) allocations[instructionIndex] = op == 15 ? (long)(short)instruction << 16 : Displacement(instruction, op == 62);
            if (op == 31 && ((instruction >> 1) & 1023) == 183 && rt == 1 && ra == 1) allocations[instructionIndex] = null;
        }
        long initialStack = stack;
        var indexedStacks = new Dictionary<int, long>();
        foreach (var allocation in allocations.OrderByDescending(p => p.Key))
        {
            if (allocation.Value is { } delta) initialStack = checked(initialStack - delta);
            else
            {
                var bytes = await readMemory(checked((uint)initialStack), 4, cancellationToken).ConfigureAwait(false);
                if (bytes.Length != 4) throw new EndOfStreamException("Missing caller stack backchain.");
                var previous = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                if (previous <= initialStack || (previous & 15) != 0) throw new InvalidDataException("Invalid caller stack backchain.");
                indexedStacks[allocation.Key] = initialStack;
                initialStack = previous;
            }
        }
        if (initialStack is < 0 or > uint.MaxValue || (initialStack & 15) != 0) throw new InvalidDataException("Invalid caller stack pointer.");
        var bases = new Dictionary<int, long> { [1] = initialStack };
        var lrRegisters = new HashSet<int>(); var crRegisters = new HashSet<int>();
        // Match the SDK context contract: retain supplied registers unless a save
        // or argument-home instruction gives a recovered value for the caller.
        var caller = current.Clone();
        var argumentsRemaining = new HashSet<int> { 3, 4, 5, 6 };
        foreach (var argument in argumentsRemaining)
        {
            var register = Enum.Parse<Register64>("r" + argument);
            if (current.Has64(register)) caller.Set64(register, current.Get64(register));
        }
        foreach (var instruction in bodyWords)
        {
            var op = instruction >> 26; var rt = (int)((instruction >> 21) & 31); var ra = (int)((instruction >> 16) & 31); var rb = (int)((instruction >> 11) & 31);
            if (op is 16 or 18 || op == 19 && ((instruction >> 1) & 1023) is 16 or 528) break;
            if (!argumentsRemaining.Contains(rt)) continue;
            ulong? value = null;
            if (op == 31 && ((instruction >> 1) & 1023) == 444 && rt == rb)
            {
                var source = Enum.Parse<Register64>("r" + ra);
                if (current.Has64(source)) value = current.Get64(source);
            }
            else if ((op is 36 or 38 || op == 62 && (instruction & 3) == 0) && ra is 1 or 31)
            {
                var source = Enum.Parse<Register64>("r" + ra);
                if (current.Has64(source))
                {
                    var address = checked((uint)((long)current.Get64(source) + Displacement(instruction, op == 62)));
                    var size = op == 38 ? 1 : op == 36 ? 4 : 8;
                    var saved = await readMemory(address, size, cancellationToken).ConfigureAwait(false);
                    if (saved.Length != size) throw new EndOfStreamException("Truncated argument home.");
                    value = size == 1 ? saved[0] : size == 4 ? BinaryPrimitives.ReadUInt32BigEndian(saved) : BinaryPrimitives.ReadUInt64BigEndian(saved);
                }
            }
            if (value is { } recovered) { caller.Set64(Enum.Parse<Register64>("r" + rt), recovered); argumentsRemaining.Remove(rt); }
        }
        uint? returnAddress = current.Has32(Register32.lr) ? current.Get32(Register32.lr) : null;
        for (var instructionIndex = 0; instructionIndex < words.Length; instructionIndex++)
        {
            var instruction = words[instructionIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var op = instruction >> 26; var rt = (int)((instruction >> 21) & 31); var ra = (int)((instruction >> 16) & 31); var rb = (int)((instruction >> 11) & 31);
            if ((instruction & 0xfc1fffff) == 0x7c0802a6) { lrRegisters.Add(rt); continue; }
            if ((instruction & 0xfc1fffff) == 0x7c000026) { crRegisters.Add(rt); continue; }
            if (op is 14 or 15)
            {
                lrRegisters.Remove(rt); crRegisters.Remove(rt);
                if (ra == 0) bases[rt] = op == 15 ? (long)(short)instruction << 16 : (short)instruction;
                else if (bases.TryGetValue(ra, out var baseAddress)) bases[rt] = baseAddress + (op == 15 ? (long)(short)instruction << 16 : (short)instruction);
                else bases.Remove(rt);
                continue;
            }
            if (op == 31 && ((instruction >> 1) & 1023) == 444 && rt == rb)
            {
                var sourceLr = lrRegisters.Contains(rt); var sourceCr = crRegisters.Contains(rt);
                lrRegisters.Remove(ra); crRegisters.Remove(ra);
                if (sourceLr) lrRegisters.Add(ra); if (sourceCr) crRegisters.Add(ra);
                if (bases.TryGetValue(rt, out var source)) bases[ra] = source; else bases.Remove(ra);
                continue;
            }
            if (op == 31 && ((instruction >> 1) & 1023) == 183 && rt == 1 && ra == 1)
            {
                bases[1] = indexedStacks[instructionIndex]; continue;
            }
            if (op == 31 && ((instruction >> 1) & 1023) == 40 && rt == 1 && rb == 1)
            {
                if (!bases.TryGetValue(ra, out var amount)) throw new NotSupportedException("Stack allocation uses an unknown register.");
                bases[1] -= amount; continue;
            }
            if (op is 36 or 37 or 54 || op == 62 && (instruction & 3) <= 1)
            {
                if (!bases.TryGetValue(ra, out var baseAddress)) throw new NotSupportedException("Save instruction uses an unknown base register.");
                var address = checked((uint)(baseAddress + Displacement(instruction, op == 62)));
                var size = op is 54 or 62 ? 8 : 4;
                if (rt != 1 || ra != 1)
                {
                    {
                        var bytes = await readMemory(address, size, cancellationToken).ConfigureAwait(false);
                        if (bytes.Length != size) throw new EndOfStreamException("Saved register is absent from memory.");
                        var bits = size == 8 ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
                        if (op == 54) caller.SetDouble(Enum.Parse<RegisterDouble>("fp" + rt), BitConverter.Int64BitsToDouble(unchecked((long)bits)));
                        else
                        {
                            caller.Set64(Enum.Parse<Register64>("r" + rt), bits);
                            if (lrRegisters.Contains(rt)) returnAddress = checked((uint)bits);
                            if (crRegisters.Contains(rt)) caller.Set32(Register32.cr, checked((uint)bits));
                        }
                    }
                }
                if (op == 37 || op == 62 && (instruction & 3) == 1) bases[ra] = address;
                continue;
            }
            if (PowerPcVmx128.TryDecode(instruction, out var vector, out var vectorRa, out var vectorRb, out var vectorLoad) && !vectorLoad)
            {
                ra = vectorRa; rb = vectorRb;
                if (!(ra == 0 || bases.ContainsKey(ra)) || !bases.TryGetValue(rb, out var index)) throw new NotSupportedException("Vector save uses an unknown address.");
                var address = checked((uint)((ra == 0 ? 0 : bases[ra]) + index)) & ~15u;
                var bytes = await readMemory(address, 16, cancellationToken).ConfigureAwait(false);
                if (bytes.Length != 16) throw new EndOfStreamException("Saved vector is absent.");
                float F(int offset) => BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))));
                caller.SetVector(Enum.Parse<RegisterVector>("v" + vector), new Vector4(F(0), F(4), F(8), F(12))); continue;
            }
            if (op == 4) continue; // VMX arithmetic has no scalar stack-pointer effects, matching native unwinding.
            if (op is 18 or 16 || op == 31 && ((instruction >> 1) & 1023) is 467 or 149) throw new NotSupportedException("This prolog requires millicode/control-flow unwinding; refusing to invent caller registers.");
            // NOP and register-only instructions do not write stack saves. Reject
            // unhandled memory operations, including store-multiple and indexed saves.
            if (op is >= 32 and <= 63 || op == 31 && ((instruction >> 1) & 1023) is 151 or 183 or 663 or 695) throw new NotSupportedException("Unrecognized PPC save instruction.");
            if (instruction != 0x60000000) throw new NotSupportedException("Unrecognized PPC prolog instruction; refusing to infer its register effects.");
        }
        if (returnAddress is null) throw new InvalidDataException("Caller link register cannot be recovered.");
        if (returnAddress == 0) return null;
        if ((returnAddress & 3) != 0 || returnAddress == pc && initialStack == stack) throw new InvalidDataException("Invalid or cyclic caller address.");
        caller.Set64(Register64.r1, (uint)initialStack); caller.Set32(Register32.iar, returnAddress.Value); caller.Set32(Register32.lr, returnAddress.Value);
        if (current.Has32(Register32.msr)) caller.Set32(Register32.msr, current.Get32(Register32.msr));
        // Parsed contexts are clean; saved memory does not imply live register edits.
        return caller.Clone();
    }
    private static int Displacement(uint instruction, bool ds) => (short)(instruction & (ds ? 0xfffcu : 0xffffu));
}
