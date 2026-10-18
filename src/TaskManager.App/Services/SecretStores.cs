using TaskManager.Core.Security;

namespace TaskManager.App.Services;

#if ANDROID
/// <summary>Android: <c>SecureStorage</c> de MAUI cifra con una clave del Android Keystore (EncryptedSharedPreferences).</summary>
public sealed class PlatformSecretStore : ISecretStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
#elif WINDOWS
/// <summary>
/// Windows: secretos cifrados con DPAPI (ámbito del usuario de Windows) en %LOCALAPPDATA%\GestorTareas\secrets.bin.
/// Sólo la misma cuenta de Windows en la misma máquina puede descifrarlos. No depende de que la app esté empaquetada (MSIX).
/// </summary>
public sealed class PlatformSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = "GestorTareas.Secrets.v1"u8.ToArray();
    private readonly string _path = Path.Combine(AppPaths.DataDirectory, "secrets.bin");
    private readonly Lock _gate = new();

    public Task<string?> GetAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(Load().GetValueOrDefault(key));
        }
    }

    public Task SetAsync(string key, string value)
    {
        lock (_gate)
        {
            var values = Load();
            values[key] = value;
            Save(values);
        }
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            var values = Load();
            if (values.Remove(key))
            {
                Save(values);
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }
        var plain = System.Security.Cryptography.ProtectedData.Unprotect(
            File.ReadAllBytes(_path), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return System.Text.Json.JsonSerializer.Deserialize(plain, SecretsJsonContext.Default.DictionaryStringString) ?? [];
    }

    private void Save(Dictionary<string, string> values)
    {
        var plain = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values, SecretsJsonContext.Default.DictionaryStringString);
        var cipher = System.Security.Cryptography.ProtectedData.Protect(
            plain, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, _path, overwrite: true);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SecretsJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
#endif

public static class AppPaths
{
    /// <summary>Carpeta privada de datos: almacenamiento interno de la app en Android, %LOCALAPPDATA%\GestorTareas en Windows.</summary>
    public static string DataDirectory
    {
        get
        {
#if WINDOWS
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestorTareas");
#else
            var dir = FileSystem.AppDataDirectory;
#endif
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "tareas.db3");
}
