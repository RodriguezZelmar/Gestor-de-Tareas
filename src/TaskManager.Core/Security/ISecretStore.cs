namespace TaskManager.Core.Security;

/// <summary>
/// Almacén de secretos del sistema operativo. En la app se implementa con <c>SecureStorage</c> de MAUI:
/// Android Keystore (EncryptedSharedPreferences) en Android y DPAPI/credenciales del usuario en Windows.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}

/// <summary>Implementación en memoria para tests.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public void Remove(string key) => _values.Remove(key);
}
