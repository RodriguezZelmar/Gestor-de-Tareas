using SQLite;

namespace TaskManager.Core.Domain;

[Table("users")]
public sealed class User
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Email normalizado (minúsculas, sin espacios).</summary>
    [NotNull, Unique, MaxLength(254)]
    public string Email { get; set; } = "";

    [NotNull, MaxLength(80)]
    public string DisplayName { get; set; } = "";

    /// <summary>Hash PBKDF2 con sal (ver <see cref="Security.PasswordHasher"/>). Nunca la contraseña.</summary>
    [NotNull]
    public string PasswordHash { get; set; } = "";

    public int FailedLogins { get; set; }

    public DateTime? LockoutUntilUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Sesión persistente. Sólo se guarda el hash SHA-256 del token; el token vive en el almacén seguro del sistema.</summary>
[Table("sessions")]
public sealed class SessionRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int UserId { get; set; }

    [NotNull, Unique]
    public string TokenHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }
}
