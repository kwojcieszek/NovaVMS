using System.Security.Cryptography;
using System.Text;
using NovaVMS.Core.Models;

namespace NovaVMS.Core.Security;

/// <summary>
/// Salted PBKDF2-SHA256 hashes of user passwords. The iteration count is stored with each hash,
/// so it can be raised later without invalidating existing passwords.
/// </summary>
public static class PasswordHasher
{
    public const int DefaultIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static void SetPassword(UserAccount user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        user.PasswordSalt = Convert.ToBase64String(salt);
        user.PasswordIterations = DefaultIterations;
        user.PasswordHash = Convert.ToBase64String(Derive(password, salt, DefaultIterations));
    }

    public static bool Verify(UserAccount user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (password is null || !user.HasPassword || user.PasswordIterations <= 0)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(user.PasswordHash);
            var actual = Derive(password, Convert.FromBase64String(user.PasswordSalt), user.PasswordIterations);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
    }
}
