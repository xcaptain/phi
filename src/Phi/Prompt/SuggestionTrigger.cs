namespace Phi.Prompt;

/// <summary>
/// Helpers that decide when a piece of editor text should trigger
/// slash-command completion. Centralised here so the suggestion strip's
/// per-keystroke fast path, the built-in providers, and the Tab-completion
/// handler all agree on the trigger rule.
/// <para>
/// The contract is intentionally strict: a slash command only triggers
/// when the <em>very first character of the editor buffer</em> is
/// <c>/</c> <em>and</em> the caret sits on that same first line (no
/// <c>\n</c> between the buffer start and the caret). Anything else —
/// leading whitespace, mid-sentence <c>/</c>, a token like <c>foo/bar</c>,
/// a command typed on a continuation line — is treated as ordinary text.
/// <para>
/// The editor itself is multi-line capable (MinHeight 3, MaxHeight 10),
/// but slash commands are inherently single-line: <c>/exit</c> is a
/// command, but typing <c>"please /exit"</c> or <c>"hello\n/exit"</c>
/// or pressing Cmd+Enter before the command finishes are all just
/// ordinary prose. This matches how the mainstream coding-agent CLIs
/// behave.
/// </para>
/// </para>
/// </summary>
public static class SuggestionTrigger
{
    /// <summary>
    /// Returns <c>true</c> when the buffer starts with <c>/</c> and the
    /// caret sits before the first newline — i.e. the user is composing
    /// a slash command on the first line of the buffer. Empty input, any
    /// input that doesn't begin with <c>/</c>, any input where the caret
    /// has crossed a newline, and a buffer that is just a single <c>/</c>
    /// followed by a newline all return <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Phase 1 of the suggestion strip assumes the caret is at the end of
    /// the input, so callers pass <c>text.Length</c> as the caret in the
    /// common case. The helper still handles mid-line carets correctly so
    /// future phases don't have to revisit it. <paramref name="caret"/>
    /// values outside the buffer range are clamped defensively.
    /// </remarks>
    public static bool StartsWithSlashOnFirstLine(string? text, int caret)
    {
        if (text is null || text.Length == 0) return false;

        // Clamp caret to the valid range so callers passing the editor's
        // (sometimes slightly out-of-range) caret index don't crash the
        // strip.
        if (caret < 0) caret = 0;
        if (caret > text.Length) caret = text.Length;

        // The trigger is the very first character of the buffer — no
        // leading whitespace allowed. If the caret has crossed a newline,
        // the user is past the first line and any '/' lives on a later
        // line, which doesn't count.
        if (caret == 0) return false;
        if (text[0] != '/') return false;

        // The first-line check: scan the prefix up to the caret for a
        // newline. As soon as one appears, the caret is no longer on the
        // first line and the trigger must not fire — even though the
        // buffer still starts with '/', the user has moved on to a
        // continuation line and that line's content isn't a command.
        for (var i = 0; i < caret; i++)
        {
            if (text[i] == '\n') return false;
        }

        return true;
    }
}
