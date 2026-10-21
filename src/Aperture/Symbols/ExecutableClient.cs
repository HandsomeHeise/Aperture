using System.Buffers.Binary;
using System.Text;
using Aperture.Transport;

namespace Aperture.Symbols;

public sealed class ExecutableClient(ConsoleConnection connection, string module)
{
    public async Task<byte[]> ReadHeaderFieldAsync(uint field, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync(FormattableString.Invariant($"xexfield module={ProtocolFields.Quote(module)} field=0x{field:X8}"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != 202 || response.Lines.Count == 0) throw new InvalidDataException("Expected multiline XEX field data.");
        var size = ProtocolFields.Parse(response.Lines[0]).GetUInt32("fieldsize");
        if (size > 64 * 1024 * 1024) throw new InvalidDataException("XEX field exceeds limits.");
        var bytes = Convert.FromHexString(string.Concat(response.Lines.Skip(1)));
        if (bytes.Length != size) throw new InvalidDataException("XEX field transfer length is inconsistent.");
        return bytes;
    }
    public async Task<uint> GetEntryPointAddressAsync(CancellationToken cancellationToken = default)
    {
        var field = await ReadHeaderFieldAsync(0x10100, cancellationToken).ConfigureAwait(false);
        if (field.Length != 4) throw new InvalidDataException("Invalid entry point field length.");
        return BinaryPrimitives.ReadUInt32BigEndian(field);
    }
    public async Task<string> GetPeModuleNameAsync(CancellationToken cancellationToken = default)
    {
        var field = await ReadHeaderFieldAsync(0x183ff, cancellationToken).ConfigureAwait(false);
        if (field.Length < 5 || BinaryPrimitives.ReadUInt32BigEndian(field) != field.Length) throw new InvalidDataException("Invalid original PE name field.");
        var length = Array.IndexOf(field, (byte)0, 4);
        if (length < 0) throw new InvalidDataException("Original PE module name is not terminated.");
        return new UTF8Encoding(false, true).GetString(field, 4, length - 4);
    }
}
