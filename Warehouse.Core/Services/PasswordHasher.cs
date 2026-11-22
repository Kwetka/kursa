using System.Security.Cryptography;

namespace Warehouse.Core.Services;

public static class PasswordHasher
{
    public const int Iterations = 210_000;
    public static (byte[] Salt, byte[] Hash) Hash(string password, int iterations = Iterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        return (salt, Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32));
    }

    public static bool Verify(string password, byte[] salt, byte[] hash, int iterations)
    {
        if (iterations <= 0 || salt.Length < 16 || hash.Length != 32) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, hash.Length);
        return CryptographicOperations.FixedTimeEquals(actual, hash);
    }
}
