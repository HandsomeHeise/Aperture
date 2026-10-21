using Aperture.Transport;

namespace Aperture.Consoles;

[Flags]
public enum UserPrivileges { None = 0, Read = 1, Write = 2, Control = 4, Configure = 8, Manage = 16 }
public sealed record ConsoleUser(string Name, UserPrivileges Privileges)
{
    public string Name { get; private set; } = Name;
    public UserPrivileges Privileges { get; private set; } = Privileges;
    internal UserAccessClient? Owner { get; init; }
    public bool HasReadAccess => Privileges.HasFlag(UserPrivileges.Read);
    public bool HasWriteAccess => Privileges.HasFlag(UserPrivileges.Write);
    public bool HasControlAccess => Privileges.HasFlag(UserPrivileges.Control);
    public bool HasConfigureAccess => Privileges.HasFlag(UserPrivileges.Configure);
    public bool HasManageAccess => Privileges.HasFlag(UserPrivileges.Manage);
    public Task SetAccessAsync(UserPrivileges access, bool enabled, CancellationToken cancellationToken = default)
    {
        if ((int)access is not (1 or 2 or 4 or 8 or 16)) throw new ArgumentOutOfRangeException(nameof(access), "Select one access property.");
        return SetPrivilegesAsync(enabled ? Privileges | access : Privileges & ~access, cancellationToken);
    }
    public async Task SetPrivilegesAsync(UserPrivileges privileges, CancellationToken cancellationToken = default)
    {
        var owner = Owner ?? throw new InvalidOperationException("This user is a detached snapshot.");
        await owner.SetPrivilegesAsync(Name, privileges, cancellationToken).ConfigureAwait(false);
        Privileges = privileges;
    }
    /// <summary>Renames by adding the new account, copying rights, and removing the old account. The monitor has no atomic rename command; failure can leave both accounts.</summary>
    public async Task RenameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var owner = Owner ?? throw new InvalidOperationException("This user is a detached snapshot.");
        if (string.Equals(Name, name, StringComparison.OrdinalIgnoreCase)) return;
        if ((await owner.ListAsync(cancellationToken).ConfigureAwait(false)).Any(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("An account with the new name already exists.");
        await owner.AddAsync(name, cancellationToken).ConfigureAwait(false);
        await owner.SetPrivilegesAsync(name, Privileges, cancellationToken).ConfigureAwait(false);
        await owner.RemoveAsync(Name, cancellationToken).ConfigureAwait(false);
        Name = name;
    }
}
public sealed class UserAccessClient(ConsoleConnection connection)
{
    private static readonly (UserPrivileges Flag, string Name)[] flags = [(UserPrivileges.Read, "read"), (UserPrivileges.Write, "write"), (UserPrivileges.Control, "control"), (UserPrivileges.Configure, "config"), (UserPrivileges.Manage, "manage")];
    public async Task<UserPrivileges> GetPrivilegesAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("getuserpriv " + (name is null ? "me" : "name=" + ProtocolFields.Quote(name)), cancellationToken).ConfigureAwait(false);
        return Parse(response.Message);
    }
    public async Task SetPrivilegesAsync(string name, UserPrivileges privileges, CancellationToken cancellationToken = default)
    {
        if (((int)privileges & ~31) != 0) throw new ArgumentOutOfRangeException(nameof(privileges));
        var names = flags.Where(f => privileges.HasFlag(f.Flag)).Select(f => f.Name).ToArray();
        _ = await connection.ExecuteAsync("setuserpriv name=" + ProtocolFields.Quote(name) + (names.Length > 0 ? " " + string.Join(' ', names) : ""), cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<ConsoleUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        var response = await connection.ExecuteAsync("userlist", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != 202) throw new InvalidDataException("Expected multiline user records.");
        return response.Lines.Select(line => new ConsoleUser(ProtocolFields.Parse(line).GetString("name"), Parse(line)) { Owner = this }).ToArray();
    }
    public async Task AddAsync(string name, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("user name=" + ProtocolFields.Quote(name), cancellationToken).ConfigureAwait(false);
    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default) => _ = await connection.ExecuteAsync("user name=" + ProtocolFields.Quote(name) + " remove", cancellationToken).ConfigureAwait(false);
    private static UserPrivileges Parse(string line)
    {
        var fields = ProtocolFields.Parse(line);
        return flags.Where(f => fields.ContainsKey(f.Name)).Aggregate(UserPrivileges.None, (value, f) => value | f.Flag);
    }
}
