using TaskManager.Core.Data;
using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.Core.Services;

/// <summary>Registro, inicio/cierre de sesión, sesiones persistentes y perfil.</summary>
public sealed class AuthService(AppDatabase db, ISecretStore secrets, PasswordHasher hasher, TimeProvider clock)
{
    internal const string SessionTokenKey = "taskmanager.session.token";
    internal const int MaxFailedLogins = 5;
    internal static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private const string InvalidCredentials = "Email o contraseña incorrectos.";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<User> RegisterAsync(string? email, string? displayName, string? password)
    {
        var normalizedEmail = InputSanitizer.Email(email);
        var name = InputSanitizer.Text(displayName, "El nombre", 80);
        InputSanitizer.Password(password);

        if (await db.Connection.Table<User>().Where(u => u.Email == normalizedEmail).CountAsync() > 0)
        {
            throw new ValidationException("Ya existe una cuenta con ese email.");
        }

        var hash = await Task.Run(() => hasher.Hash(password!));
        var user = new User { Email = normalizedEmail, DisplayName = name, PasswordHash = hash, CreatedUtc = Now, UpdatedUtc = Now };
        await db.Connection.InsertAsync(user);
        await SeedAsync(user);
        await StartSessionAsync(user.Id);
        return user;
    }

    public async Task<User> LoginAsync(string? email, string? password)
    {
        string normalizedEmail;
        try
        {
            normalizedEmail = InputSanitizer.Email(email);
        }
        catch (ValidationException)
        {
            throw new AuthException(InvalidCredentials);
        }

        var user = await db.Connection.Table<User>().Where(u => u.Email == normalizedEmail).FirstOrDefaultAsync();
        if (user is null)
        {
            // Mismo costo que una verificación real para no revelar qué emails existen.
            await Task.Run(() => hasher.Verify(password ?? "", _dummyHash.Value, out _));
            throw new AuthException(InvalidCredentials);
        }

        if (user.LockoutUntilUtc is { } until && until.AsUtc() > Now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((until.AsUtc() - Now).TotalMinutes));
            throw new AuthException($"Demasiados intentos fallidos. Probá de nuevo en {minutes} min.");
        }

        var needsRehash = false;
        var ok = await Task.Run(() => hasher.Verify(password ?? "", user.PasswordHash, out needsRehash));
        if (!ok)
        {
            user.FailedLogins++;
            if (user.FailedLogins >= MaxFailedLogins)
            {
                user.FailedLogins = 0;
                user.LockoutUntilUtc = Now + LockoutDuration;
            }
            await db.Connection.UpdateAsync(user);
            throw new AuthException(InvalidCredentials);
        }

        user.FailedLogins = 0;
        user.LockoutUntilUtc = null;
        if (needsRehash)
        {
            user.PasswordHash = await Task.Run(() => hasher.Hash(password!));
        }
        await db.Connection.UpdateAsync(user);
        await StartSessionAsync(user.Id);
        return user;
    }

    /// <summary>Recupera la sesión guardada en el almacén seguro, si sigue vigente. Renueva su vencimiento.</summary>
    public async Task<User?> RestoreSessionAsync()
    {
        var token = await secrets.GetAsync(SessionTokenKey);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var tokenHash = Tokens.Sha256Hex(token);
        var session = await db.Connection.Table<SessionRecord>().Where(s => s.TokenHash == tokenHash).FirstOrDefaultAsync();
        if (session is null || session.ExpiresUtc.AsUtc() <= Now)
        {
            if (session is not null)
            {
                await db.Connection.DeleteAsync(session);
            }
            secrets.Remove(SessionTokenKey);
            return null;
        }

        var user = await db.Connection.FindAsync<User>(session.UserId);
        if (user is null)
        {
            secrets.Remove(SessionTokenKey);
            return null;
        }

        session.ExpiresUtc = Now + SessionLifetime;
        await db.Connection.UpdateAsync(session);
        return user;
    }

    public async Task LogoutAsync()
    {
        var token = await secrets.GetAsync(SessionTokenKey);
        if (!string.IsNullOrEmpty(token))
        {
            var tokenHash = Tokens.Sha256Hex(token);
            await db.Connection.Table<SessionRecord>().DeleteAsync(s => s.TokenHash == tokenHash);
        }
        secrets.Remove(SessionTokenKey);
    }

    public async Task<User> UpdateProfileAsync(int userId, string? displayName, string? email)
    {
        var user = await db.Connection.FindAsync<User>(userId) ?? throw new NotFoundException("El usuario");
        var normalizedEmail = InputSanitizer.Email(email);
        var name = InputSanitizer.Text(displayName, "El nombre", 80);

        if (normalizedEmail != user.Email
            && await db.Connection.Table<User>().Where(u => u.Email == normalizedEmail).CountAsync() > 0)
        {
            throw new ValidationException("Ya existe una cuenta con ese email.");
        }

        user.Email = normalizedEmail;
        user.DisplayName = name;
        user.UpdatedUtc = Now;
        await db.Connection.UpdateAsync(user);
        return user;
    }

    /// <summary>Cambia la contraseña y revoca todas las sesiones; abre una nueva para este dispositivo.</summary>
    public async Task ChangePasswordAsync(int userId, string? currentPassword, string? newPassword)
    {
        var user = await db.Connection.FindAsync<User>(userId) ?? throw new NotFoundException("El usuario");
        if (!await Task.Run(() => hasher.Verify(currentPassword ?? "", user.PasswordHash, out _)))
        {
            throw new AuthException("La contraseña actual no es correcta.");
        }
        InputSanitizer.Password(newPassword);

        user.PasswordHash = await Task.Run(() => hasher.Hash(newPassword!));
        user.UpdatedUtc = Now;
        await db.Connection.UpdateAsync(user);
        await db.Connection.Table<SessionRecord>().DeleteAsync(s => s.UserId == userId);
        await StartSessionAsync(userId);
    }

    /// <summary>Elimina la cuenta y todos sus datos. Requiere la contraseña.</summary>
    public async Task DeleteAccountAsync(int userId, string? password)
    {
        var user = await db.Connection.FindAsync<User>(userId) ?? throw new NotFoundException("El usuario");
        if (!await Task.Run(() => hasher.Verify(password ?? "", user.PasswordHash, out _)))
        {
            throw new AuthException("La contraseña no es correcta.");
        }

        var projectIds = (await db.Connection.Table<Project>().Where(p => p.OwnerId == userId).ToListAsync()).Select(p => p.Id).ToList();
        var labelIds = (await db.Connection.Table<Label>().Where(l => l.OwnerId == userId).ToListAsync()).Select(l => l.Id).ToList();
        await db.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var projectId in projectIds)
            {
                ProjectService.DeleteProjectRows(conn, projectId);
            }
            foreach (var labelId in labelIds)
            {
                conn.Table<TaskLabel>().Delete(tl => tl.LabelId == labelId);
            }
            conn.Table<Label>().Delete(l => l.OwnerId == userId);
            conn.Table<Comment>().Delete(c => c.AuthorId == userId);
            conn.Table<SessionRecord>().Delete(s => s.UserId == userId);
            conn.Delete<User>(userId);
        });
        secrets.Remove(SessionTokenKey);
    }

    private async Task StartSessionAsync(int userId)
    {
        var token = Tokens.NewSessionToken();
        await db.Connection.InsertAsync(new SessionRecord
        {
            UserId = userId,
            TokenHash = Tokens.Sha256Hex(token),
            CreatedUtc = Now,
            ExpiresUtc = Now + SessionLifetime,
        });
        await secrets.SetAsync(SessionTokenKey, token);
    }

    /// <summary>Etiquetas por defecto y un tablero de bienvenida para cuentas nuevas.</summary>
    private async Task SeedAsync(User user)
    {
        var labels = new[]
        {
            new Label { OwnerId = user.Id, Name = "Bug", Color = "#E53935" },
            new Label { OwnerId = user.Id, Name = "Mejora", Color = "#43A047" },
            new Label { OwnerId = user.Id, Name = "Diseño", Color = "#8E24AA" },
            new Label { OwnerId = user.Id, Name = "Documentación", Color = "#1E88E5" },
        };
        await db.Connection.InsertAllAsync(labels);

        var project = new Project
        {
            OwnerId = user.Id,
            Name = "Mi primer tablero",
            Description = "Un tablero de ejemplo. Podés editarlo o borrarlo.",
            Color = "#3F51B5",
            CreatedUtc = Now,
            UpdatedUtc = Now,
        };
        await db.Connection.InsertAsync(project);

        var samples = new (string Title, TaskState State, Priority Priority, int? DueDays, int Label)[]
        {
            ("Explorar el tablero", TaskState.ToDo, Priority.High, 1, 3),
            ("Crear una etiqueta propia", TaskState.ToDo, Priority.Low, 7, 1),
            ("Arrastrar una tarea a otra columna", TaskState.InProgress, Priority.Medium, null, 2),
            ("Crear la cuenta", TaskState.Done, Priority.Medium, null, 1),
        };
        var position = new Dictionary<TaskState, int>();
        foreach (var s in samples)
        {
            var pos = position.GetValueOrDefault(s.State);
            position[s.State] = pos + 1;
            var task = new TaskItem
            {
                ProjectId = project.Id,
                Title = s.Title,
                State = s.State,
                Priority = s.Priority,
                DueUtc = s.DueDays is { } d ? Now.Date.AddDays(d).AddHours(18) : null,
                Position = pos,
                CreatedUtc = Now,
                UpdatedUtc = Now,
                CompletedUtc = s.State == TaskState.Done ? Now : null,
            };
            await db.Connection.InsertAsync(task);
            await db.Connection.InsertAsync(new TaskLabel { TaskId = task.Id, LabelId = labels[s.Label].Id });
        }
    }

    private readonly Lazy<string> _dummyHash = new(() => hasher.Hash("dummy-password-0"));
}
