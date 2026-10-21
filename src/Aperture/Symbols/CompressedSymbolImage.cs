using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
namespace Aperture.Symbols;

/// <summary>Expands standard symbol-store CAB/SZDD archives using the Windows operating system utility, without SDK or native library bindings.</summary>
internal static class CompressedSymbolImage
{
    internal static async Task<byte[]> ExpandAsync(byte[] archive, string expectedName, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Compressed symbol-store images require the Windows operating system's expand utility.");
        uint expectedLength;
        var cabinet = archive.AsSpan().StartsWith("MSCF"u8);
        if (cabinet)
        {
            if (archive.Length < 36 || BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(8)) != archive.Length || BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(28)) != 1 || (BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(30)) & 3) != 0) throw new InvalidDataException("A symbol cabinet must contain one file and no linked cabinets.");
            var fileOffset = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(16));
            if (fileOffset > archive.Length - 17) throw new InvalidDataException("The symbol cabinet's file table is invalid.");
            expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan((int)fileOffset));
            var nameOffset = (int)fileOffset + 16; var terminator = Array.IndexOf(archive, (byte)0, nameOffset);
            if (terminator < 0 || terminator - nameOffset > 255) throw new InvalidDataException("The symbol cabinet's filename is invalid.");
            var filename = Encoding.Latin1.GetString(archive, nameOffset, terminator - nameOffset);
            if (!string.Equals(filename, expectedName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The symbol cabinet contains an unexpected filename.");
        }
        else
        {
            if (archive.Length < 14 || !archive.AsSpan(0, 8).SequenceEqual(new byte[] { 0x53, 0x5a, 0x44, 0x44, 0x88, 0xf0, 0x27, 0x33 })) throw new InvalidDataException("The symbol-store compressed image is not a CAB or SZDD archive.");
            expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(10));
        }
        if (expectedLength > SymbolServerImageResolver.MaximumImageBytes) throw new InvalidDataException("The expanded symbol image exceeds 64 MiB.");
        var directory = Path.Combine(Path.GetTempPath(), "aperture-expand-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.cab");
        var outputDirectory = Path.Combine(directory, "expanded"); Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, expectedName);
        try
        {
            await File.WriteAllBytesAsync(input, archive, ct).ConfigureAwait(false);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (cabinet) start.ArgumentList.Add("-R");
            start.ArgumentList.Add(input);
            if (cabinet) start.ArgumentList.Add("-F:*");
            start.ArgumentList.Add(cabinet ? outputDirectory : output);
            using var process = Process.Start(start) ?? throw new IOException("The Windows expand utility could not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
            try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); throw; }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidDataException("The Windows utility could not expand the symbol image: " + (await stderr.ConfigureAwait(false)) + (await stdout.ConfigureAwait(false)));
            if (new FileInfo(output).Length != expectedLength) throw new InvalidDataException("The expanded symbol image has an unexpected size.");
            return await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(outputDirectory);
            Directory.Delete(directory);
        }
    }
}
