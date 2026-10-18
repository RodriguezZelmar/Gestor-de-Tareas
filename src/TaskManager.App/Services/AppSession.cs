using TaskManager.Core.Data;
using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.App.Services;

/// <summary>Base abierta y usuario actual. Los servicios del dominio se resuelven recién después de <see cref="OpenDatabaseAsync"/>.</summary>
public sealed class AppSession(ISecretStore secrets)
{
    private AppDatabase? _db;
    private User? _user;

    public AppDatabase Database => _db ?? throw new InvalidOperationException("La base de datos todavía no está abierta.");

    public User User => _user ?? throw new InvalidOperationException("No hay sesión iniciada.");

    public int UserId => User.Id;

    public bool IsSignedIn => _user is not null;

    /// <summary>Alertas ya mostradas en esta sesión, para no repetir el aviso al volver a la lista de tableros.</summary>
    public bool AlertsShown { get; set; }

    public event EventHandler? UserChanged;

    public async Task OpenDatabaseAsync() => _db ??= await AppDatabase.OpenAsync(AppPaths.DatabasePath, secrets);

    public async Task ResetDatabaseAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
            _db = null;
        }
        AppDatabase.Reset(AppPaths.DatabasePath, secrets);
    }

    public void SetUser(User? user)
    {
        _user = user;
        AlertsShown = false;
        UserChanged?.Invoke(this, EventArgs.Empty);
    }
}
