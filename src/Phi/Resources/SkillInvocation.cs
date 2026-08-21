using System.Text.RegularExpressions;

namespace Phi.Resources;

/// <summary>
/// A skill invocation parsed out of a user message, mirroring pi's
/// <c>ParsedSkillBlock</c> (agent-session.ts). <see cref="Content"/> is the
/// SKILL.md body plus the anchor line; <see cref="UserMessage"/> is any
/// trailing args after the block, or null when absent.
/// </summary>
public sealed record SkillBlock(
    string Name,
    string Location,
    string Content,
    string? UserMessage);

/// <summary>
/// The wire format for a skill loaded into the conversation, matching pi's
/// <c>&lt;skill&gt;</c> block. The block is injected as a single user message
/// and parsed back by <see cref="TryParse"/> so the UI can render it as a
/// collapsible card instead of raw XML text:
/// <code>
/// &lt;skill name="..." location="..."&gt;
/// References are relative to &lt;skillDir&gt;.
///
/// &lt;body&gt;
/// &lt;/skill&gt;
/// </code>
/// Trailing args (from <c>/skill:NAME &lt;args&gt;</c>) ride after the block
/// as the parsed <see cref="SkillBlock.UserMessage"/>.
/// </summary>
public static partial class SkillInvocation
{
    // Port of pi's parseSkillBlock regex (packages/coding-agent/.../agent-session.ts):
    //   /^<skill name="([^"]+)" location="([^"]+)">\n([\s\S]*?)\n<\/skill>(?:\n\n([\s\S]+))?$/
    private static readonly Regex BlockRegex = MyRegex();

    public static string Build(string name, string location, string baseDir, string body, string? args = null)
    {
        var block = $"<skill name=\"{name}\" location=\"{location}\">\n" +
            $"References are relative to {baseDir}.\n\n" +
            $"{body}\n</skill>";
        return args is { Length: > 0 } ? $"{block}\n\n{args}" : block;
    }

    /// <summary>
    /// Attempts to parse <paramref name="text"/> as a skill invocation.
    /// Returns false when the text is not a well-formed <c>&lt;skill&gt;</c>
    /// block. Line endings are normalized so messages stored on any platform
    /// parse identically.
    /// </summary>
    public static bool TryParse(string text, out SkillBlock? block)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = BlockRegex.Match(text.Replace("\r\n", "\n"));
        if (!match.Success)
        {
            block = null;
            return false;
        }

        var userMessage = match.Groups[4].Success ? match.Groups[4].Value.Trim() : "";
        block = new SkillBlock(
            Name: match.Groups[1].Value,
            Location: match.Groups[2].Value,
            Content: match.Groups[3].Value,
            UserMessage: userMessage.Length == 0 ? null : userMessage);
        return true;
    }

    [GeneratedRegex(@"^<skill name=""([^""]+)"" location=""([^""]+)"">\n([\s\S]*?)\n</skill>(?:\n\n([\s\S]+))?$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
