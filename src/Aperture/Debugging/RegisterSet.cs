using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Aperture.Models;
using Aperture.Transport;

namespace Aperture.Debugging;

public sealed partial class RegisterSet
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> modified = new(StringComparer.OrdinalIgnoreCase);
    public bool Dirty => modified.Count > 0;
    public RegisterSet Clone() => Parse(values.Select(p => p.Key + "=" + p.Value).ToArray());
    public static RegisterSet Parse(string[] lines)
    {
        var context = new RegisterSet();
        foreach (var line in lines)
            foreach (Match field in FieldsRegex().Matches(line)) context.values[field.Groups[1].Value] = field.Groups[2].Value;
        return context;
    }
    public bool Has32(Register32 register) => values.ContainsKey(Name(register));
    public bool Has64(Register64 register) => values.ContainsKey(Name(register));
    public bool HasDouble(RegisterDouble register) => values.ContainsKey(Name(register));
    public bool HasVector(RegisterVector register) => values.ContainsKey(Name(register));
    public uint Get32(Register32 register) => ProtocolFields.UInt32(Get(Name(register)));
    public ulong Get64(Register64 register) => ProtocolFields.UInt64(Get(Name(register)));
    public double GetDouble(RegisterDouble register) => BitConverter.Int64BitsToDouble(unchecked((long)ProtocolFields.UInt64(Get(Name(register)))));
    public Vector4 GetVector(RegisterVector register)
    {
        var components = Get(Name(register)).Split(',');
        if (components.Length != 4) throw new InvalidDataException("Vector register does not contain four components.");
        var f = components.Select(v => BitConverter.Int32BitsToSingle(unchecked((int)ProtocolFields.UInt32(v)))).ToArray();
        return new(f[0], f[1], f[2], f[3]);
    }
    public void Set32(Register32 register, uint value) => Set(Name(register), "0x" + value.ToString("X8", CultureInfo.InvariantCulture));
    public void Set64(Register64 register, ulong value) => Set(Name(register), "0q" + value.ToString("X16", CultureInfo.InvariantCulture));
    public void SetDouble(RegisterDouble register, double value) => Set(Name(register), "0q" + unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16", CultureInfo.InvariantCulture));
    public void SetVector(RegisterVector register, Vector4 value) => Set(Name(register), string.Join(',', new[] { value.X, value.Y, value.Z, value.W }.Select(v => "0x" + unchecked((uint)BitConverter.SingleToInt32Bits(v)).ToString("X8", CultureInfo.InvariantCulture))));
    public async Task FlushAsync(ConsoleConnection connection, uint threadId, CancellationToken cancellationToken = default)
    {
        if (!Dirty) return;
        var snapshot = modified.ToArray();
        foreach (var chunk in snapshot.Chunk(12))
        {
            var command = FormattableString.Invariant($"setcontext thread=0x{threadId:X8} ") + string.Join(' ', chunk.Select(p => p.Key + "=" + p.Value));
            _ = await connection.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            foreach (var field in chunk) if (modified.TryGetValue(field.Key, out var latest) && latest == field.Value) modified.Remove(field.Key);
        }
    }
    private string Get(string key) => values.TryGetValue(key, out var value) ? value : throw new InvalidOperationException("Register is not available in this stack frame: " + key);
    private void Set(string key, string value) { values[key] = value; modified[key] = value; }
    private static string Name(Register32 register) => Enum.IsDefined(register) ? register.ToString() : throw new ArgumentOutOfRangeException(nameof(register));
    private static string Name(Register64 register) => !Enum.IsDefined(register) ? throw new ArgumentOutOfRangeException(nameof(register)) : register == Register64.ctr ? "Ctr" : "Gpr" + register.ToString()[1..];
    private static string Name(RegisterDouble register) => !Enum.IsDefined(register) ? throw new ArgumentOutOfRangeException(nameof(register)) : register == RegisterDouble.fpscr ? "Fpscr" : "Fpr" + register.ToString()[2..];
    private static string Name(RegisterVector register) => !Enum.IsDefined(register) ? throw new ArgumentOutOfRangeException(nameof(register)) : register.ToString() == "vscr" ? "Vscr" : "Vr" + register.ToString()[1..];
    [GeneratedRegex(@"([A-Za-z][A-Za-z0-9]*)=([^\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FieldsRegex();
}
