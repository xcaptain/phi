using Phi.Prompt;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace Phi.Tui.Components;

/// <summary>
/// Live autocomplete panel that sits between the prompt editor and the status
/// bar. Recomputes on every change to the editor's bound text (dependency
/// tracking on the <c>State</c>) and asks each <see cref="ISuggestionProvider"/>
/// in turn for candidates; renders the first non-empty result as a compact
/// two-row bar:
/// <list type="bullet">
/// <item>row 1: horizontal chips of the matching commands (best match
/// highlighted, wrapped when they don't fit, <c>+N more</c> when truncated);</item>
/// <item>row 2: the best match's name and description.</item>
/// </list>
/// Collapses to nothing when no provider matches. Same structure as the
/// upstream <c>PromptEditorDemo</c> suggestion bar. Phase 1 assumes the caret
/// sits at the end of the input.
/// <para>
/// Every keystroke re-runs the builder (XenoAtom's dependency tracking fires
/// on any change to <see cref="Text"/>), so the builder's fast path skips the
/// provider pipeline whenever the buffer doesn't start with <c>/</c> on
/// the first line — the common case when the user types a normal prompt,
/// a mid-sentence slash like <c>"please /exit"</c>, or a command typed on
/// a continuation line like <c>"hello\n/exit"</c>. The check walks at
/// most the first-line prefix of the buffer, <c>O(caret)</c>, vs. the
/// per-provider work which allocates a candidate list and walks the
/// full command catalog. Both built-in providers gate on the same
/// "buffer starts with <c>/</c> on the first line" rule (see
/// <see cref="SuggestionTrigger.StartsWithSlashOnFirstLine"/>), so the
/// short-circuit is sound for the current provider set and for any
/// provider that follows the same contract.
/// </para>
/// </summary>
public sealed class SuggestionStrip
{
    private const int MaxVisibleItems = 10;

    private readonly IReadOnlyList<ISuggestionProvider> _providers;
    private readonly ComputedVisual _visual;

    public SuggestionStrip(State<string?> text, IReadOnlyList<ISuggestionProvider> providers)
    {
        Text = text;
        _providers = providers;
        _visual = new ComputedVisual(Build);
        Visual = _visual;
    }

    /// <summary>The editor's bound text; setting it re-evaluates the strip.</summary>
    public State<string?> Text { get; }

    public Visual Visual { get; }

    /// <summary>Match for the latest evaluation, if any. Inspection/tests.</summary>
    public SuggestionMatch? CurrentMatch { get; private set; }

    /// <summary>
    /// Computes the first provider's suggestion for the given input. Exposed
    /// so tests can drive the pure filtering logic without rendering.
    /// </summary>
    public SuggestionMatch? ComputeMatch(string? text, int caret)
    {
        var normalized = text ?? "";
        foreach (var provider in _providers)
        {
            if (provider.GetSuggestion(normalized, caret) is { } match)
            {
                return match;
            }
        }
        return null;
    }

    private VStack? Build()
    {
        var text = Text.Value ?? "";

        // Fast path: the buffer doesn't start with '/' on the first line
        // (e.g. the user is typing a normal prompt, a mid-sentence '/'
        // like "please /exit", or has crossed onto a continuation line).
        // Skip the provider pipeline entirely — no foreach over the
        // command catalog, no candidate list allocation. The built-in
        // providers (SlashCommandProvider, SkillSuggestionProvider) gate
        // on the same first-line-slash rule, so any provider following
        // that contract will also reject this input. The check walks at
        // most the prefix up to the caret (bounded by the first newline,
        // which is at most the editor's first line): O(caret), typically
        // under a few dozen chars.
        CurrentMatch = null;
        if (!IsActiveSlashOnFirstLine(text))
        {
            return null;
        }

        var match = ComputeMatch(text, text.Length);
        CurrentMatch = match;
        if (match is null || match.Items.Count == 0)
        {
            return null;
        }

        var items = match.Items;
        var theme = _visual.GetTheme();
        var accent = theme.Accent ?? theme.Primary ?? theme.Foreground;
        var chipBg = (accent ?? Color.Default).WithAlpha(0x22);
        var chipBgActive = (accent ?? Color.Default).WithAlpha(0x38);

        // Row 1: command chips (best match highlighted).
        var chips = new List<Visual> { new Markup("[dim]Commands:[/]") { Wrap = false } };
        var shown = 0;
        for (var i = 0; i < items.Count && shown < MaxVisibleItems; i++, shown++)
        {
            var isBest = i == 0;
            chips.Add(
                new TextBlock($" {items[i].Label} ").Style(TextBlockStyle.Default with
                {
                    Background = isBest ? chipBgActive : chipBg,
                    FillBackground = true,
                    TextStyle = isBest ? TextStyle.Bold : default,
                }));
        }

        var more = items.Count > MaxVisibleItems
            ? new Markup($"[dim]+{items.Count - MaxVisibleItems} more…[/]") { Wrap = false }
            : null;

        // Row 2: best match's name + description.
        var best = items[0];
        var details = new Markup(
            $"[dim]↳[/] [primary]{Escape(best.Label)}[/] [dim]- {Escape(best.Description)}[/]")
        {
            Wrap = false,
        };

        var wrap = new WrapHStack(chips.ToArray()).Spacing(1).RunSpacing(0);
        return new VStack(
                wrap,
                more is null ? details : new HStack(details, more).Spacing(1))
            .Spacing(0);
    }

    private static string Escape(string text) =>
        text.Replace("[", "\\[").Replace("]", "\\]");

    /// <summary>
    /// Returns <c>true</c> when the buffer starts with <c>/</c> on the
    /// first line — the strict trigger both built-in providers gate on.
    /// A slash buried mid-sentence (<c>"please /exit"</c>), a leading
    /// whitespace (<c>"  /exit"</c>), or a slash on a continuation line
    /// (<c>"hello\n/exit"</c>) all fail to qualify; only a buffer whose
    /// very first character is <c>/</c> <em>and</em> whose caret has not
    /// yet crossed a newline triggers the provider pipeline. Delegates to
    /// <see cref="SuggestionTrigger.StartsWithSlashOnFirstLine"/> so the
    /// strip and the providers can never drift apart.
    /// </summary>
    internal static bool IsActiveSlashOnFirstLine(string text) =>
        SuggestionTrigger.StartsWithSlashOnFirstLine(text, text.Length);
}
