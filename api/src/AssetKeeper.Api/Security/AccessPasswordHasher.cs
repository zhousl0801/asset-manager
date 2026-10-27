using System.Globalization;
using System.Security.Cryptography;

namespace AssetKeeper.Api.Security;

/// <summary>
/// PBKDF2-SHA256 口令散列，格式为 pbkdf2-sha256$迭代次数$盐$散列。
/// </summary>
public static class AccessPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int MinimumIterations = 10_000;
    private const int MaximumIterations = 1_000_000;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (iterations < MinimumIterations || iterations > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Derive(password, salt, iterations);
        return string.Join(
            '$',
            Prefix,
            iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public static bool Verify(string? password, string? encodedHash)
    {
        if (string.IsNullOrEmpty(password) || !TryParse(encodedHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
    }

    private static bool TryParse(string? encodedHash, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];

        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Trim().Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < MinimumIterations
            || iterations > MaximumIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            key = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length is >= 8 and <= 64 && key.Length is >= 16 and <= 64;
    }
}
