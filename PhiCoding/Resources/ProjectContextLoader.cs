using System.Text;
using PhiCoding.Prompts;

namespace PhiCoding.Resources;

/// <summary>
/// Discovers project-instructions files (<c>AGENTS.md</c>) for a session
/// and packages them into a <see cref="SessionResources"/> snapshot.
/// <para>
/// Search scope: the project root (as located by
/// <see cref="ProjectRootLocator"/>) down to <c>cwd</c>, inclusive. The
/// loader deliberately does not consult <c>~/.phi</c>, <c>~/.agents</c>,
/// or <c>&lt;dir&gt;/.phi</c> / <c>&lt;dir&gt;/.agents</c> — global or
/// per-directory agent-config directories are out of scope for project
/// instructions.
/// </para>
/// <para>
/// Each candidate file is capped at <see cref="MaxFileSizeBytes"/> bytes
/// to keep the system prompt bounded; over-size files are skipped with a
/// <see cref="DiagnosticSeverity.Warning"/> diagnostic. Read failures
/// (permission, transient IO) are also warned about but never thrown —
/// AGENTS.md is best-effort and must not block session start.
/// </para>
/// <para>
/// File content is treated as plain UTF-8 text. YAML frontmatter (if
/// present) is not parsed; the entire body is forwarded to the prompt
/// builder verbatim. Frontmatter-aware loading lands with the skills
/// loader in Phase 5.
/// </para>
/// </summary>
public sealed class ProjectContextLoader
{
    public const int MaxFileSizeBytes = 64 * 1024;

    private const string AgentsFileName = "AGENTS.md";

    public static SessionResources Load(SessionResourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var cwd = Path.GetFullPath(options.Cwd);
        var projectRoot = ProjectRootLocator.Locate(cwd);

        var dirs = CollectDirectories(projectRoot, cwd);
        var files = new List<ProjectContextFile>(dirs.Count);
        var diagnostics = new List<ResourceDiagnostic>();

        foreach (var dir in dirs)
        {
            TryLoadAgentsMd(dir, files, diagnostics);
        }

        return new SessionResources
        {
            ContextFiles = files,
            Diagnostics = diagnostics,
        };
    }

    private static List<string> CollectDirectories(string? projectRoot, string cwd)
    {
        var stop = projectRoot ?? cwd;
        var comparison = PathComparison;

        var collected = new List<string>();
        var dir = cwd;
        while (true)
        {
            collected.Add(dir);
            if (string.Equals(dir, stop, comparison))
                break;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }
        collected.Reverse();
        return collected;
    }

    private static void TryLoadAgentsMd(
        string dir,
        List<ProjectContextFile> sink,
        List<ResourceDiagnostic> diagnostics)
    {
        var path = Path.Combine(dir, AgentsFileName);
        if (!File.Exists(path))
            return;

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileSizeBytes)
            {
                diagnostics.Add(new ResourceDiagnostic(
                    Source: $"AGENTS.md:{path}",
                    Message: $"AGENTS.md at {path} is {info.Length} bytes, exceeding the {MaxFileSizeBytes}-byte limit; skipped.",
                    Severity: DiagnosticSeverity.Warning));
                return;
            }

            var content = File.ReadAllText(path, Encoding.UTF8);
            sink.Add(new ProjectContextFile(path, content));
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ResourceDiagnostic(
                Source: $"AGENTS.md:{path}",
                Message: $"Failed to read {path}: {ex.Message}",
                Severity: DiagnosticSeverity.Warning));
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
