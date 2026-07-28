using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace PhiCoding.Tui;

/// <summary>
/// Single source of truth for all Phi TUI colors and spacing. Each
/// <see cref="PhiTheme"/> instance exposes raw color slots, padding values,
/// and factories that build v2 <see cref="Scheme"/>s for the major view
/// regions — change anything visual here, not in the views themselves.
///
/// Spacing mirrors CSS flex's box model:
/// <list type="bullet">
/// <item><see cref="TranscriptPadding"/> / <see cref="PromptPadding"/> /
/// <see cref="StatusPadding"/> — inner padding (CSS <c>padding</c>) applied
/// to each region.</item>
/// <item><see cref="TranscriptMargin"/> / <see cref="PromptMargin"/> /
/// <see cref="StatusMargin"/> — outer margin (CSS <c>margin</c>). Useful
/// for vertical gaps between stacked regions.</item>
/// </list>
/// Terminal.Gui has no single <c>gap</c> property between siblings — set
/// <see cref="Margin"/> on the children instead, v2 sums them like flex.
/// </summary>
public sealed class PhiTheme
{
    // Raw palette — change here, propagates everywhere.
    public required Color Background { get; init; }
    public required Color TranscriptBackground { get; init; }
    public required Color PromptBackground { get; init; }
    public required Color StatusBackground { get; init; }

    public required Color Foreground { get; init; }
    public required Color PromptForeground { get; init; }
    public required Color StatusForeground { get; init; }

    public required Color FocusedBorder { get; init; }

    public required Color UserPrefix { get; init; }
    public required Color AssistantText { get; init; }
    public required Color ToolCall { get; init; }
    public required Color ToolOk { get; init; }
    public required Color ToolError { get; init; }
    public required Color ToolOutput { get; init; }
    public required Color DiffAdded { get; init; }
    public required Color DiffRemoved { get; init; }
    public required Color DiffMeta { get; init; }

    // Spacing — CSS padding (inside the view border).
    public required Thickness TranscriptPadding { get; init; }
    public required Thickness PromptPadding { get; init; }
    public required Thickness StatusPadding { get; init; }

    // Spacing — CSS margin (outside the view, between siblings).
    public required Thickness TranscriptMargin { get; init; }
    public required Thickness PromptMargin { get; init; }
    public required Thickness StatusMargin { get; init; }

    /// <summary>Map a <see cref="TranscriptStyle"/> to an <see cref="Attribute"/> for manual rendering.</summary>
    public Attribute AttributeFor(TranscriptStyle style)
    {
        var bg = TranscriptBackground;
        return style switch
        {
            TranscriptStyle.User => new Attribute(UserPrefix, bg, TextStyle.Bold),
            TranscriptStyle.Assistant => new Attribute(AssistantText, bg),
            TranscriptStyle.ToolCall => new Attribute(ToolCall, bg),
            TranscriptStyle.ToolOk => new Attribute(ToolOk, bg),
            TranscriptStyle.ToolError => new Attribute(ToolError, bg, TextStyle.Bold),
            TranscriptStyle.ToolOutput => new Attribute(DiffMeta, bg),
            TranscriptStyle.DiffAdded => new Attribute(DiffAdded, bg),
            TranscriptStyle.DiffRemoved => new Attribute(DiffRemoved, bg),
            TranscriptStyle.DiffMeta => new Attribute(DiffMeta, bg),
            TranscriptStyle.Status => new Attribute(DiffMeta, bg, TextStyle.Italic),
            TranscriptStyle.Error => new Attribute(ToolError, bg, TextStyle.Bold),
            _ => new Attribute(Foreground, bg),
        };
    }

    /// <summary>Scheme applied to the <c>Window</c> itself (fallback background).</summary>
    public Scheme WindowScheme() => new(new Attribute(Foreground, Background));

    /// <summary>Scheme for the transcript view; sets its background fill.</summary>
    public Scheme TranscriptScheme()
    {
        var normal = new Attribute(Foreground, TranscriptBackground);
        return new Scheme(normal)
        {
            Focus = new Attribute(Foreground, TranscriptBackground),
        };
    }

    /// <summary>Scheme for the prompt input — distinct background so the user can see where the input box is.</summary>
    public Scheme PromptScheme()
    {
        var normal = new Attribute(PromptForeground, PromptBackground);
        return new Scheme(normal)
        {
            Focus = new Attribute(PromptForeground, PromptBackground, TextStyle.Bold),
            Editable = new Attribute(PromptForeground, PromptBackground),
            HotFocus = new Attribute(FocusedBorder, PromptBackground, TextStyle.Bold),
        };
    }

    /// <summary>Scheme for the status line.</summary>
    public Scheme StatusScheme() => new(new Attribute(StatusForeground, StatusBackground));

    /// <summary>The default dark theme used when none is supplied.</summary>
    public static PhiTheme DefaultDark() => new()
    {
        Background = new Color(0x1E, 0x1E, 0x1E),
        TranscriptBackground = new Color(0x1E, 0x1E, 0x1E),
        PromptBackground = new Color(0x2D, 0x2D, 0x2D),   // slightly lighter → input box stands out
        StatusBackground = new Color(0x18, 0x18, 0x18),

        Foreground = new Color(StandardColor.White),
        PromptForeground = new Color(StandardColor.White),
        StatusForeground = new Color(StandardColor.BrightBlack),

        FocusedBorder = new Color(StandardColor.Cyan),

        UserPrefix = new Color(StandardColor.BrightCyan),
        AssistantText = new Color(StandardColor.White),
        ToolCall = new Color(StandardColor.Cyan),
        ToolOk = new Color(StandardColor.Green),
        ToolError = new Color(StandardColor.Red),
        ToolOutput = new Color(StandardColor.BrightBlack),
        DiffAdded = new Color(StandardColor.Green),
        DiffRemoved = new Color(StandardColor.Red),
        DiffMeta = new Color(StandardColor.BrightBlack),

        // Inner padding: transcript and prompt breathe a little from the
        // window edge. Status stays flush (one-line bar).
        TranscriptPadding = new Thickness(2, 0, 2, 0),
        PromptPadding = new Thickness(2, 0, 2, 0),
        StatusPadding = new Thickness(1, 0, 1, 0),

        // Outer margins: small vertical gaps between stacked regions so
        // the three regions don't touch each other directly.
        TranscriptMargin = new Thickness(0, 0, 0, 0),
        PromptMargin = new Thickness(0, 1, 0, 0),
        StatusMargin = new Thickness(0, 0, 0, 1),
    };
}