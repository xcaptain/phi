using Phi.Prompt;

namespace Phi.Tests.Tui;

public class SuggestionTriggerTests
{
    [Test]
    public async Task EmptyAndWhitespace_NeverTrigger()
    {
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine(null, 0)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("", 0)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("", 5)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("   ", 3)).IsFalse();
    }

    [Test]
    public async Task CaretZero_NeverTriggers()
    {
        // There is no character before the caret to inspect; the trigger
        // requires the buffer's first character to be '/' (and caret > 0).
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/exit", 0)).IsFalse();
    }

    [Test]
    public async Task BufferStartsWithSlash_OnFirstLine_Triggers()
    {
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/", 1)).IsTrue();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/exit", 5)).IsTrue();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/skill:foo", 11)).IsTrue();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/connect openai", 15)).IsTrue();
    }

    [Test]
    public async Task LeadingWhitespace_DoesNotTrigger()
    {
        // The contract is "the very first character of the buffer is '/'",
        // with no tolerance for leading whitespace.
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine(" /exit", 6)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("  /exit", 7)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("\t/exit", 6)).IsFalse();
    }

    [Test]
    public async Task MidSentenceSlash_DoesNotTrigger()
    {
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello /exit", 11)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("foo/bar", 7)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("path/to/x", 9)).IsFalse();
    }

    [Test]
    public async Task ContinuationLine_DoesNotTrigger()
    {
        // Multi-line input: only the first line participates. A '/' on a
        // continuation line is ordinary text even though the buffer still
        // starts with '/'.
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello\n/exit", 11)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello\n/", 7)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/exit\nfoo", 9)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/exit\n", 6)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/exit\n/skill", 12)).IsFalse();
    }

    [Test]
    public async Task CaretBeyondLength_IsClampedSafely()
    {
        // Defensive: callers occasionally pass caret == text.Length (the
        // common case in the suggestion strip), but we also tolerate caret
        // running past the end without throwing or spuriously triggering.
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("/exit", 99)).IsTrue();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello", 99)).IsFalse();
    }

    [Test]
    public async Task CaretAfterTrailingNewline_DoesNotReadPastEnd()
    {
        // Regression for an IndexOutOfRangeException hit when Cmd+Enter /
        // Shift+Enter inserts a '\n' and parks the caret right after it.
        // The caret is on a (yet-to-be-typed) second line and there's no
        // first-line slash to inspect; the trigger must cleanly return
        // false instead of reading past the end of the buffer.
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello\n", 6)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("\n", 1)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello\n", 7)).IsFalse();
        await Assert.That(SuggestionTrigger.StartsWithSlashOnFirstLine("hello\n\n", 7)).IsFalse();
    }
}
