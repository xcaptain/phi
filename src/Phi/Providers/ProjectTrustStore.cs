using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phi.Providers;

/// <summary>
/// User-level trust decisions for project extensions, persisted at
/// <c>~/.phi/trust.json</c>. One entry per project cwd key — the user
/// either approved or declined the project's extension set, and whether
/// the decision should be remembered (so they don't get re-prompted on
/// every session start).
/// <para>
/// Sprint 3b foundation: this is the storage layer. The decision
/// surface (the confirm dialog) lives in the composition root (TUI /
/// Avalonia) so each host can match its own UX — TUI uses
/// <see cref="IPhiUiBridge.ShowConfirmAsync"/>; Avalonia pops a
/// <see cref="Avalonia.Controls.Window"/>; headless mode (CI) auto-approves.
/// </para>
/// </summary>
public sealed record ProjectTrustStore
{
    /// <summary>
    /// Per-cwd trust decisions. Key is <see cref="ProjectKey"/>; value
    /// captures the choice + the extension names the decision covered +
    /// whether to remember it.
    /// </summary>
    public Dictionary<string, ProjectTrustDecision> Decisions { get; init; } = new();

    public static string DefaultPath => Path.Combine(SessionPaths.PhiHome, "trust.json");

    public static ProjectTrustStore Load(string? path = null)
    {
        var actual = path ?? DefaultPath;
        if (!File.Exists(actual)) return new ProjectTrustStore();
        try
        {
            return JsonSerializer.Deserialize(
                    File.ReadAllText(actual), ProjectTrustJsonContext.Default.ProjectTrustStore)
                ?? new ProjectTrustStore();
        }
        catch (JsonException)
        {
            // Corrupt file → start fresh rather than crash the host.
            return new ProjectTrustStore();
        }
    }

    public void Save(string? path = null)
    {
        var actual = path ?? DefaultPath;
        var parent = Path.GetDirectoryName(actual);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(actual,
            JsonSerializer.Serialize(this, ProjectTrustJsonContext.Default.ProjectTrustStore) + "\n");
    }

    /// <summary>
    /// Returns the prior decision for <paramref name="cwdKey"/>, or null
    /// if the user has never been asked. Callers decide whether a null
    /// result means "auto-approve" (headless) or "ask now" (TUI / Avalonia).
    /// </summary>
    public ProjectTrustDecision? Lookup(string cwdKey) =>
        Decisions.TryGetValue(cwdKey, out var d) ? d : null;
}

/// <summary>
/// Single project-trust decision. Mirrors
/// <c>Phi.Extensions.ExtensionTrustDecision</c> but lives in Phi core so
/// the storage layer has no dependency on Phi.Extensions.Host.
/// </summary>
public sealed record ProjectTrustDecision(
    ProjectTrustKind Kind,
    DateTimeOffset DecidedAt,
    IReadOnlyList<string> ExtensionNames,
    bool Remember);

public enum ProjectTrustKind { Approve, Decline }

/// <summary>Source-generated serializer so we don't ship a reflection
/// dependency on <see cref="JsonSerializer"/> at runtime.</summary>
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectTrustStore))]
internal partial class ProjectTrustJsonContext : JsonSerializerContext { }