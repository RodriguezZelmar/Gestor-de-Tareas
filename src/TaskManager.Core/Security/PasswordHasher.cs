using System.Globalization;
using System.Security.Cryptography;

namespace TaskManager.Core.Security;

/// <summary>
/// Hash de contraseñas con PBKDF2-HMAC-SHA256 y sal aleatoria por usuario.
/// Formato: <c>pbkdf2-sha256$iteraciones$salBase64$hashBase64</c>, así se pueden subir las iteraciones sin romper hashes viejos.
/// </summary>
public sealed class PasswordHasher(int iterations = PasswordHasher.DefaultIterations)
{
    /// <summary>Recomendación OWASP para PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Scheme, iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Verifica en tiempo constante. <paramref name="needsRehash"/> indica que el hash usa parámetros viejos.</summary>
    public bool Verify(string password, string stored, out bool needsRehash)
    {
        needsRehash = false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var storedIterations)
            || storedIterations <= 0)
        {
            return false;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, storedIterations, HashAlgorithmName.SHA256, expected.Length);
        var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        needsRehash = ok && storedIterations < iterations;
        return ok;
    }
}

public static class Tokens
{
    /// <summary>Token de sesión: 256 bits aleatorios en Base64Url.</summary>
    public static string NewSessionToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
