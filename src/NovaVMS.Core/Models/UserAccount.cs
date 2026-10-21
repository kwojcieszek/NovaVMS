namespace NovaVMS.Core.Models;

/// <summary>
/// Application user. Part of the configuration, so users are synchronized with the settings database.
/// Only a salted password hash is stored (see <see cref="Security.PasswordHasher"/>).
/// </summary>
public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public int PasswordIterations { get; set; }

    public UserPermissions Permissions { get; set; }

    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash) && !string.IsNullOrEmpty(PasswordSalt);

    public bool Has(UserPermissions permission) => (Permissions & permission) == permission;

    public UserAccount Copy() => (UserAccount)MemberwiseClone();
}
