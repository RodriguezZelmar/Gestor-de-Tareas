using System.Security.Cryptography;

using SQLite;

using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.Core.Data;

/// <summary>
/// Base SQLite cifrada en reposo con SQLCipher (AES-256). La clave es aleatoria por instalación y se guarda
/// en el almacén seguro del sistema (<see cref="ISecretStore"/>), nunca en disco junto a la base.
/// </summary>
public sealed class AppDatabase : IAsyncDisposable
{
    internal const string KeyName = "taskmanager.db.key.v1";

    private AppDatabase(SQLiteAsyncConnection connection, string path)
    {
        Connection = connection;
        Path = path;
    }

    public SQLiteAsyncConnection Connection { get; }

    public string Path { get; }

    public static async Task<AppDatabase> OpenAsync(string path, ISecretStore secrets)
    {
        var keyHex = await secrets.GetAsync(KeyName);
        if (keyHex is null)
        {
            if (File.Exists(path))
            {
                // Hay datos pero se perdió la clave (p. ej. se borró el almacén seguro): son irrecuperables.
                throw new DatabaseKeyException("No se encontró la clave de cifrado de la base de datos existente.");
            }
            keyHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await secrets.SetAsync(KeyName, keyHex);
        }

        var options = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: Convert.FromHexString(keyHex));
        var connection = new SQLiteAsyncConnection(options);
        try
        {
            // Con una clave incorrecta SQLCipher falla en la primera lectura ("file is not a database").
            await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master");
            await connection.CreateTablesAsync(CreateFlags.None,
                typeof(User), typeof(SessionRecord), typeof(Project), typeof(Label), typeof(TaskItem), typeof(TaskLabel), typeof(Comment));
        }
        catch (SQLiteException ex)
        {
            await connection.CloseAsync();
            throw new DatabaseKeyException("No se pudo abrir la base de datos cifrada.", ex);
        }
        return new AppDatabase(connection, path);
    }

    /// <summary>
    /// Borra la base y su clave. Sólo para el flujo de "restablecer datos" cuando la clave se perdió.
    /// La conexión a ese archivo tiene que estar cerrada antes (<see cref="DisposeAsync"/>).
    /// </summary>
    public static void Reset(string path, ISecretStore secrets)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            File.Delete(file);
        }
        secrets.Remove(KeyName);
    }

    public ValueTask DisposeAsync() => new(Connection.CloseAsync());
}

public sealed class DatabaseKeyException(string message, Exception? inner = null) : Exception(message, inner);
