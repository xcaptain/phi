using System.Text.Json;

namespace Phi.Extensions.Host;

/// <summary>
/// Append-only JSONL audit log at <see cref="SessionPaths.PhiHome"/>/audit.log
/// (resolves to <c>~/.phi/audit.log</c> in production). One line per
/// audit-worthy event:
/// <list type="bullet">
/// <item>Extension loaded / setup succeeded / setup failed.</item>
/// <item>Capability mismatch (extension invoked an <see cref="IPhiApi"/>
/// action whose required capability isn't declared on its
/// <see cref="PhiExtensionAttribute"/>).</item>
/// <item>Project trust decision (loaded / declined / remembered).</item>
/// </list>
/// <para>
/// v1 is silent for everything except capability mismatches; the log is
/// rotated weekly and never read by the host. v1.5 lets
/// <c>--show-audit</c> tail it live for the user. The directory +
/// file are created lazily on first write; missing <c>~/.phi</c> means
/// the user's data dir hasn't been touched yet, which is fine.
/// </para>
/// <para>
/// Writes are <c>File.AppendAllText</c> — simple, lock-free across
/// processes, slow under heavy contention. Phi is single-session per
/// process in v1 so this isn't a hot path. If v2 needs concurrent
/// writers, swap in <c>FileStream</c> with <c>FileShare.Read</c> and a
/// single producer queue.
/// </para>
/// </summary>
internal static class AuditLogger
{
    private static readonly object Gate = new();
    private static string? _cachedPath;

    /// <summary>Absolute path of the audit log file.</summary>
    public static string Path
    {
        get
        {
            if (_cachedPath is not null) return _cachedPath;
            // Path comes from SessionPaths.PhiHome — the same constant
            // sessions, the trust store, and DeskLog use. There's no
            // env-var override: production always uses ~/.phi, tests
            // redirect by setting SessionPaths.PhiHome to a temp dir.
            _cachedPath = System.IO.Path.Combine(SessionPaths.PhiHome, "audit.log");
            return _cachedPath;
        }
    }

    /// <summary>
    /// Append a single JSONL line. Safe to call from any thread; serialised
    /// on a private lock to prevent line interleaving.
    /// </summary>
    public static void Write(AuditEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var json = JsonSerializer.Serialize(ev, AuditJson.Options);
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, json + Environment.NewLine);
            }
            catch
            {
                // Audit failures must never break the host — a full disk,
                // read-only home, or revoked sandbox perms should not
                // cascade into a runtime exception. Drop the line; the
                // session continues.
            }
        }
    }

    private static class AuditJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            // Compact one-line records — easier to grep, tail, and
            // import into a real log store later.
            WriteIndented = false,
            // Lowercase keys (`"kind"`, `"extension"`, `"method"`) so
            // grep commands like `grep '"kind":"capability"'` work
            // without needing awk / sed.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }
}

/// <summary>One audit record. Fields are intentionally flat + lowercase
/// so the JSONL is grep-friendly (<c>grep '"kind":"capability"'</c>).
/// </summary>
internal sealed record AuditEvent(
    string Kind,
    DateTimeOffset Timestamp,
    string Extension,
    string Method,
    string? Detail)
{
    /// <summary>Capability mismatch: extension invoked an action whose
    /// required capability isn't declared on its PhiExtension attribute.</summary>
    public static AuditEvent CapabilityMismatch(string ext, string method, ExtensionCapability required, ExtensionCapability declared)
        => new("capability_mismatch", DateTimeOffset.UtcNow, ext, method,
            $"required={required}; declared={declared}");

    /// <summary>Capability violation under strict mode (v1.5+):
        /// an action was blocked because the extension lacks the cap.</summary>
    public static AuditEvent CapabilityBlocked(string ext, string method, ExtensionCapability required, ExtensionCapability declared)
        => new("capability_blocked", DateTimeOffset.UtcNow, ext, method,
            $"required={required}; declared={declared}");

    /// <summary>Extension loaded successfully.</summary>
    public static AuditEvent ExtensionLoaded(string ext, string version, string assemblyPath)
        => new("extension_loaded", DateTimeOffset.UtcNow, ext, "", $"v{version}; path={assemblyPath}");

    /// <summary>Extension Setup() succeeded.</summary>
    public static AuditEvent ExtensionSetupOk(string ext) =>
        new("extension_setup_ok", DateTimeOffset.UtcNow, ext, "", null);

    /// <summary>Extension Setup() threw; the runtime swallowed it and
    /// recorded the exception in <c>SetupResults</c>.</summary>
    public static AuditEvent ExtensionSetupFailed(string ext, string error) =>
        new("extension_setup_failed", DateTimeOffset.UtcNow, ext, "", error);

    /// <summary>Project trust decision: which extensions were approved /
    /// declined, and whether the user asked to remember.</summary>
    public static AuditEvent ProjectTrust(string cwd, string decision, bool remember, IReadOnlyList<string> extensions)
        => new("project_trust", DateTimeOffset.UtcNow, "", "", $"cwd={cwd}; decision={decision}; remember={remember}; extensions={string.Join(",", extensions)}");
}