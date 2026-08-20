using System.Text.Json;

namespace Phi.Providers;

/// <summary>
/// Default <see cref="ICredentialStore"/> backed by a plaintext JSON file at
/// <c>~/.phi/credentials.json</c>. The file is written atomically (temp file
/// + rename) and locked down to the owning user (<c>0600</c> on Unix) — the
/// same protection model as <c>~/.ssh</c> private keys. Secrets are not
/// encrypted at rest; that is the OS-keyring store's job if one is added.
/// </summary>
public sealed class FileCredentialStore(string filePath) : ICredentialStore
{
    private readonly string _filePath = filePath;
    private readonly Lock _lock = new();

    /// <summary>Default location under Phi home.</summary>
    public static string DefaultPath => Path.Combine(SessionPaths.PhiHome, "credentials.json");

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_lock)
        {
            var data = Load();
            return data.TryGetValue(name, out var value) ? value : null;
        }
    }

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Credential value must not be empty", nameof(value));

        lock (_lock)
        {
            var data = Load();
            data[name] = value;
            Save(data);
        }
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_lock)
        {
            var data = Load();
            if (!data.Remove(name)) return;
            Save(data);
        }
    }

    public bool Has(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_lock)
        {
            return Load().ContainsKey(name);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            return JsonSerializer.Deserialize(
                    File.ReadAllText(_filePath), PhiJsonContext.Default.DictionaryStringString)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(Dictionary<string, string> data)
    {
        var parent = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        var content = JsonSerializer.Serialize(data, PhiJsonContext.Default.DictionaryStringString) + "\n";
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, content);
        RestrictPermissions(temp);
        File.Move(temp, _filePath, overwrite: true);
        RestrictPermissions(_filePath);
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
