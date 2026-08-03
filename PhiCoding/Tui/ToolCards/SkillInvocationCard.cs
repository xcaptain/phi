using PhiCoding.Resources;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;

namespace PhiCoding.Tui.ToolCards;

/// <summary>
/// Collapsible card for a skill invocation user message, mirroring pi's
/// <c>SkillInvocationMessageComponent</c>: a <c>[skill]</c> button header that
/// toggles between a collapsed single line and the full skill body rendered
/// as markdown. The hidden body uses <see cref="Visual.IsVisible"/>, so a
/// collapsed card takes up no vertical space.
/// </summary>
public sealed class SkillInvocationCard
{
    private readonly string _name;
    private readonly Markup _headerText = new("");
    private readonly MarkdownControl _body;
    private bool _expanded;

    public SkillInvocationCard(SkillBlock block)
    {
        _name = block.Name;
        _headerText.Text = HeaderMarkup(_expanded);

        _body = new MarkdownControl(block.Content)
        {
            IsVisible = false,
            Margin = new Thickness(2, 0, 2, 0),
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Start,
            Options = MarkdownRenderOptions.Default with
            {
                MaxCodeBlockHeight = 10,
                WrapText = true,
            },
        };

        var toggle = new Button(_headerText)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Start,
        };
        toggle.ClickRouted += (_, _) => Toggle();

        Visual = new Group(toggle, _body)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start)
            .Padding(1);
    }

    public Visual Visual { get; }

    private string HeaderMarkup(bool expanded)
    {
        var hint = expanded ? "· click / Enter to collapse" : "· click / Enter to expand";
        return $"[dim][skill][/] [primary]{ToolCardBase.Escape(_name)}[/] [dim]{hint}[/]";
    }

    private void Toggle()
    {
        _expanded = !_expanded;
        _headerText.Text = HeaderMarkup(_expanded);
        _body.IsVisible = _expanded;
    }
}
