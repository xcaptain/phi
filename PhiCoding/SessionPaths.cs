using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PhiCoding;

/// <summary>
/// Filesystem layout for persisted sessions, modelled after tau's
/// per-project session directories. Layout:
/// <code>
/// {root}/{slug}-{hash[:6]}/index.jsonl     # per-project session index
/// {root}/{slug}-{hash[:6]}/{sessionId}.jsonl
/// </code>
/// The <c>{slug}</c> is a human-readable path segment (e.g.
/// <c>home-github-phi</c>); the <c>{hash}</c> is a stable 6-char hex suffix
/// that disambiguates projects with the same leaf name.
/// </summary>
public static class SessionPaths
{
    private const string DefaultRootSegment = "sessions";
    private const string IndexFileName = "index.jsonl";

    /// <summary>
    /// Unique, human-readable project key for <paramref name="cwd"/>.
    /// Format: <c>{slug}-{sha256(cwd)[:6]}</c>. Example:
    /// <c>home-github-phi-a1b2c3</c>.
    /// </summary>
    public static string ProjectKey(string cwd)
    {
        var resolved = Path.GetFullPath(cwd);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resolved));
        return $"{SlugifyPath(resolved)}-{Convert.ToHexString(hash)[..6].ToLowerInvariant()}";
    }

    /// <summary>
    /// Root directory for all sessions of the project at <paramref name="cwd"/>.
    /// </summary>
    public static string SessionRootFor(string cwd) =>
        Path.Combine(DefaultRoot, ProjectKey(cwd));

    /// <summary>Index file for the project at <paramref name="cwd"/>.</summary>
    public static string IndexFileFor(string cwd) =>
        Path.Combine(SessionRootFor(cwd), IndexFileName);

    /// <summary>Session JSONL file for the given project cwd and session id.</summary>
    public static string SessionFileFor(string cwd, string sessionId) =>
        Path.Combine(SessionRootFor(cwd), $"{sessionId}.jsonl");

    /// <summary>
    /// Stable default-session id for a project. Format: <c>default-{sha256(name)[:8]}</c>
    /// where name is the <see cref="ProjectKey"/>.
    /// </summary>
    public static string DefaultSessionId(string cwd) =>
        "default-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(ProjectKey(cwd)))
        )[..8].ToLowerInvariant();

    /// <summary>Creates the session root directory. Idempotent.</summary>
    public static string EnsureRootFor(string cwd)
    {
        var root = SessionRootFor(cwd);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string DefaultRoot =>
        Path.Combine(PhiHome, DefaultRootSegment);

    public static string PhiHome =>
        Environment.GetEnvironmentVariable("PHI_HOME")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".phi");

    /// <summary>
    /// Converts an absolute path to a readable slug for use in directory names.
    /// Replaces non-alphanumeric chars with <c>-</c>, lowercases, prefixes
    /// with <c>home-</c> when under the user's home directory.
    /// Ported from tau's <c>tau_coding.paths._slugify_path</c>.
    /// </summary>
    private static string SlugifyPath(string path)
    {
        var parts = path.Split('/').Where(p => p.Length > 0).ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.Ordinal))
        {
            parts = ["home", .. path[home.Length..].TrimStart('/').Split('/')];
        }

        var slugParts = parts
            .Select(p => Regex.Replace(p, @"[^a-zA-Z0-9._-]+", "-").Trim('-', '.', '_').ToLowerInvariant())
            .Where(p => p.Length > 0)
            .ToList();

        var slug = string.Join("-", slugParts);
        const int maxLength = 72;
        if (slug.Length <= maxLength) return slug;

        // Trim from the end to fit maxLength
        var suffixParts = new List<string>();
        var suffixLen = 0;
        for (var i = slugParts.Count - 1; i >= 0; i--)
        {
            var next = suffixLen + slugParts[i].Length + (suffixParts.Count > 0 ? 1 : 0);
            if (next > maxLength) break;
            suffixParts.Insert(0, slugParts[i]);
            suffixLen = next;
        }
        return string.Join("-", suffixParts).Length > 0
            ? string.Join("-", suffixParts)
            : slug[..Math.Min(slug.Length, maxLength)].Trim('-');
    }
}
