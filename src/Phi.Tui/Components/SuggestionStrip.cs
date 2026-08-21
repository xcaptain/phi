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
}
