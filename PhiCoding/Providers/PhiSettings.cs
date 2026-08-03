using System.Text.Json;

namespace PhiCoding.Providers;

/// <summary>
/// User-level provider preferences persisted at <c>~/.phi/settings.json</c>:
/// which provider was connected last and the active model. Non-secret — API
/// keys live in the credential store. An empty <c>DefaultProvider</c> means
/// "never configured"; the caller falls back to the catalog's first entry.
/// </summary>
public sealed record PhiSettings
{
    public string DefaultProvider { get; init; } = "";
    public string DefaultModel { get; init; } = "";

    /// <summary>Default location under Phi home.</summary>
    public static string DefaultPath => Path.Combine(SessionPaths.PhiHome, "settings.json");

    public static PhiSettings Load(string path)
    {
        if (!File.Exists(path)) return new PhiSettings();
        try
        {
            return JsonSerializer.Deserialize(
                    File.ReadAllText(path), PhiJsonContext.Default.PhiSettings)
                ?? new PhiSettings();
        }
        catch (JsonException)
        {
            return new PhiSettings();
        }
    }

    public static void Save(string path, PhiSettings settings)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        var content = JsonSerializer.Serialize(settings, PhiJsonContext.Default.PhiSettings) + "\n";
        File.WriteAllText(path, content);
    }
}
