using System.Globalization;
using Aperture.Transport;

namespace Aperture.Files;

public sealed record ConsoleFileInfo(string Name, ulong Size, bool IsDirectory, bool IsReadOnly, DateTimeOffset? CreationTime, DateTimeOffset? ChangeTime, bool IsHidden = false);
public sealed record DriveSpace(ulong FreeToCaller, ulong TotalBytes, ulong TotalFreeBytes);
public enum FileCreateDisposition { OpenExisting, OpenAlways, CreateNew }
public sealed class ConsoleFileSystem(ConsoleConnection connection)
{
    public async Task<IReadOnlyList<ConsoleFileInfo>> ListAsync(string directory, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("dirlist name=" + ProtocolFields.Quote(directory), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != 202) throw new InvalidDataException("Expected directory records.");
        return response.Lines.Select(line => ParseInfo(line)).ToArray();
    }
    public async Task<ConsoleFileInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("getfileattributes name=" + ProtocolFields.Quote(path), cancellationToken).ConfigureAwait(false);
        return ParseInfo(response.StatusCode == 202 ? string.Join(' ', response.Lines) : response.Message, path);
    }
    public async Task<byte[]> ReadBytesAsync(string path, uint offset, uint count, CancellationToken cancellationToken = default)
    {
        if ((ulong)offset + count > 0x1_0000_0000UL || count > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return [];
        var result = await connection.ReceiveLengthPrefixedAsync(FormattableString.Invariant($"getfile name={ProtocolFields.Quote(path)} offset={offset} size={count}"), cancellationToken).ConfigureAwait(false);
        if (result.Length > count) throw new InvalidDataException("File response exceeds the requested range.");
        return result;
    }
    public async Task<int> WriteBytesAsync(string path, uint offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if ((ulong)offset + (uint)data.Length > 0x1_0000_0000UL) throw new ArgumentOutOfRangeException(nameof(data));
        if (data.IsEmpty) return 0;
        var response = await connection.SendBinaryAsync(FormattableString.Invariant($"writefile name={ProtocolFields.Quote(path)} offset={offset} length={data.Length}"), data, cancellationToken).ConfigureAwait(false);
        var written = int.TryParse(response.Message, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : data.Length;
        if (written != data.Length) throw new IOException("File write completed only partially.");
        return written;
    }
    public async Task DownloadAsync(string remotePath, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var info = await GetInfoAsync(remotePath, cancellationToken).ConfigureAwait(false);
        if (info.IsDirectory) throw new IOException("Cannot download a directory as a file.");
        if (info.Size > uint.MaxValue) throw new IOException("The monitor's 32-bit file-transfer protocol cannot address this file.");
        uint offset = 0;
        while (offset < info.Size)
        {
            var count = (uint)Math.Min(1024 * 1024UL, info.Size - offset);
            var bytes = await ReadBytesAsync(remotePath, offset, count, cancellationToken).ConfigureAwait(false);
            if (bytes.Length != count) throw new EndOfStreamException("Remote file changed or returned a partial transfer.");
            await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false); offset += count;
        }
    }
    public async Task DownloadAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        await using var destination = new FileStream(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await DownloadAsync(remotePath, destination, cancellationToken).ConfigureAwait(false);
    }
    public async Task UploadAsync(Stream source, string remotePath, FileCreateDisposition disposition = FileCreateDisposition.CreateNew, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await SetSizeAsync(remotePath, 0, disposition, cancellationToken).ConfigureAwait(false);
        var buffer = new byte[1024 * 1024]; uint offset = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            await WriteBytesAsync(remotePath, offset, buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            offset = checked(offset + (uint)count);
        }
    }
    public async Task UploadAsync(string localPath, string remotePath, FileCreateDisposition disposition = FileCreateDisposition.CreateNew, CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await UploadAsync(source, remotePath, disposition, cancellationToken).ConfigureAwait(false);
    }
    public async Task SetSizeAsync(string path, uint size, FileCreateDisposition disposition = FileCreateDisposition.OpenExisting, CancellationToken cancellationToken = default)
    {
        var suffix = disposition switch { FileCreateDisposition.OpenExisting => "", FileCreateDisposition.OpenAlways => " cancreate", FileCreateDisposition.CreateNew => " mustcreate", _ => throw new ArgumentOutOfRangeException(nameof(disposition)) };
        _ = await connection.ExecuteAsync(FormattableString.Invariant($"fileeof name={ProtocolFields.Quote(path)} size={size}{suffix}"), cancellationToken).ConfigureAwait(false);
    }
    public async Task MakeDirectoryAsync(string path, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("mkdir name=" + ProtocolFields.Quote(path), cancellationToken).ConfigureAwait(false);
    public async Task RemoveDirectoryAsync(string path, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("delete name=" + ProtocolFields.Quote(path) + " dir", cancellationToken).ConfigureAwait(false);
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("delete name=" + ProtocolFields.Quote(path), cancellationToken).ConfigureAwait(false);
    public async Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("rename name=" + ProtocolFields.Quote(path) + " newname=" + ProtocolFields.Quote(newPath), cancellationToken).ConfigureAwait(false);
    public async Task<IReadOnlyList<string>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("drivelist", cancellationToken).ConfigureAwait(false);
        return response.Lines.Select(line => ProtocolFields.Parse(line).GetString("drivename")).ToArray();
    }
    public async Task<DriveSpace> GetDriveSpaceAsync(string drive, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("drivefreespace name=" + ProtocolFields.Quote(drive), cancellationToken).ConfigureAwait(false);
        var fields = ProtocolFields.Parse(response.StatusCode == 202 ? string.Join(' ', response.Lines) : response.Message);
        return new(Combine(fields, "freetocaller"), Combine(fields, "totalbytes"), Combine(fields, "totalfreebytes"));
    }
    public async Task SetAttributesAsync(string path, bool isReadOnly, bool? isHidden = null, DateTimeOffset? creationTime = null, DateTimeOffset? changeTime = null, CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(path, cancellationToken).ConfigureAwait(false);
        var creation = unchecked((ulong)(creationTime ?? info.CreationTime ?? DateTimeOffset.FromFileTime(0)).ToFileTime());
        var change = unchecked((ulong)(changeTime ?? info.ChangeTime ?? DateTimeOffset.FromFileTime(0)).ToFileTime());
        _ = await connection.ExecuteAsync(FormattableString.Invariant($"setfileattributes name={ProtocolFields.Quote(path)} createhi=0x{creation >> 32:X8} createlo=0x{(uint)creation:X8} changehi=0x{change >> 32:X8} changelo=0x{(uint)change:X8} readonly={(isReadOnly ? 1 : 0)} hidden={(isHidden ?? info.IsHidden ? 1 : 0)}"), cancellationToken).ConfigureAwait(false);
    }
    private static ConsoleFileInfo ParseInfo(string line, string fallbackName = "")
    {
        var fields = ProtocolFields.Parse(line);
        return new(fields.GetString("name", fallbackName), Combine(fields, "size"), Flag(fields, "directory"), Flag(fields, "readonly"), Time(fields, "create"), Time(fields, "change"), Flag(fields, "hidden"));
    }
    private static bool Flag(IReadOnlyDictionary<string, string> fields, string key) => fields.TryGetValue(key, out var value) && (value.Length == 0 || ProtocolFields.UInt32(value) != 0);
    private static ulong Combine(IReadOnlyDictionary<string, string> fields, string name) => ((ulong)fields.GetUInt32(name + "hi") << 32) | fields.GetUInt32(name + "lo");
    private static DateTimeOffset? Time(IReadOnlyDictionary<string, string> fields, string name) => fields.ContainsKey(name + "lo") ? DateTimeOffset.FromFileTime(checked((long)Combine(fields, name))) : null;
}
