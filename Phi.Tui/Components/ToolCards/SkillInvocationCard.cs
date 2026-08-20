using Phi.Resources;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Card for a skill invocation user message. The <c>[skill] name</c> label
/// sits in the group header; the body previews the first
/// <see cref="PreviewLines"/> lines. When the body is longer, a bottom
/// "click / Enter to expand" button reveals the full content (and collapses
/// it again); shorter bodies are shown in full with no button.
/// </summary>
public sealed class SkillInvocationCard
{
    internal const int PreviewLines = 5;

    private readonly string _fullContent;
    private readonly string _preview;
    private readonly bool _hasMore;
    private readonly MarkdownControl _body;
    private readonly Button? _toggle;
    private readonly Markup _toggleText = new("");
    private bool _expanded;

    public SkillInvocationCard(SkillBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        _fullContent = block.Content;
        _preview = Preview(_fullContent, PreviewLines, out _hasMore);

        _body = new MarkdownControl(_preview)
        {
            Margin = new Thickness(2, 0, 2, 0),
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Start,
            Options = MarkdownRenderOptions.Default with
            {
                MaxCodeBlockHeight = 10,
                WrapText = true,
            },
        };

        var header = new Markup($"[dim][skill][/] [primary]{ToolCardBase.Escape(block.Name)}[/]")
        {
            Wrap = false,
        };

        if (_hasMore)
        {
            _toggleText.Text = ToggleMarkup(_expanded);
            _toggle = new Button(_toggleText)
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Start,
            };
            _toggle.ClickRouted += (_, _) => Toggle();

            Visual = new Group(header, new VStack(_body, _toggle).Spacing(1))
                .HorizontalAlignment(Align.Stretch)
                .VerticalAlignment(Align.Start)
                .Padding(1);
        }
        else
        {
            Visual = new Group(header, _body)
                .HorizontalAlignment(Align.Stretch)
                .VerticalAlignment(Align.Start)
                .Padding(1);
        }
    }

    public Visual Visual { get; }

    private static string ToggleMarkup(bool expanded) =>
        expanded ? "[dim]click / Enter to collapse[/]" : "[dim]click / Enter to expand[/]";

    private void Toggle()
    {
        _expanded = !_expanded;
        _body.Markdown = _expanded ? _fullContent : _preview;
        _toggleText.Text = ToggleMarkup(_expanded);
    }

    /// <summary>
    /// Returns the first <paramref name="previewLines"/> lines of
    /// <paramref name="content"/>; <paramref name="hasMore"/> reports whether
    /// lines were dropped. Line endings are normalized to <c>\n</c>.
    /// </summary>
    internal static string Preview(string content, int previewLines, out bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        hasMore = lines.Length > previewLines;
        return hasMore ? string.Join('\n', lines.Take(previewLines)) : normalized;
    }
}
