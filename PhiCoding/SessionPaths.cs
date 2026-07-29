namespace PhiCoding;

/// <summary>
/// Filesystem layout helpers for persisted sessions. The default layout
/// is <c>~/.phi/sessions/</c> (override <c>PHI_HOME</c> for tests / portable
/// installs), but the methods here that construct paths take a <c>root</c>
/// argument explicitly — env-var lookups happen only at the
/// <see cref="PhiHome"/> convenience, so library code can pass a known
/// root and stay decoupled from process environment.
/// <para>
/// Layout:
/// <code>
/// {root}/index.jsonl          # one SessionRecord per line
/// {root}/{sessionId}.jsonl    # one SessionEntry per line
/// </code>
/// </para>
/// </summary>
public static class SessionPaths
{
    private const string DefaultRootSegment = "sessions";
    private const string IndexFileName = "index.jsonl";

    public static string IndexFileIn(string root) =>
        Path.Combine(root, IndexFileName);

    public static string SessionFileIn(string root, string sessionId) =>
        Path.Combine(root, $"{sessionId}.jsonl");

    /// <summary>Creates the root directory if missing. Idempotent.</summary>
    public static void EnsureRoot(string root) => Directory.CreateDirectory(root);

    public static string DefaultRoot =>
        Path.Combine(PhiHome, DefaultRootSegment);

    public static string PhiHome =>
        Environment.GetEnvironmentVariable("PHI_HOME")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".phi");
}

