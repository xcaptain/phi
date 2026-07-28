using System.Text;
using PhiAgent;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;

namespace PhiCoding.Tui;

/// <summary>
/// The scrolling conversation view: a <see cref="DocumentFlow"/> of chat cards.
/// Assistant text streams into a per-turn <see cref="MarkdownControl"/>;
/// tool calls render as bordered cards that update in place when the
/// tool result arrives. Mirrors the XenoAtom Playground sample.
/// </summary>
public sealed class ChatTranscript
{
    private readonly DocumentFlow _flow;
    private readonly Dictionary<string, ToolCard> _toolCards = new();
    private StringBuilder? _streamText;
    private MarkdownControl? _streamControl;

    public ChatTranscript()
    {
        _flow = new DocumentFlow
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            ItemPadding = new Thickness(1, 0, 1, 0),
            ItemSpacing = 1,
            FollowTail = true,
            MaxCapacity = 500,
        };
    }

    public Visual Visual => _flow;

    public void Apply(HarnessEvent ev)
    {
        switch (ev)
        {
            case AssistantTextDeltaEvent d:
                AppendDelta(d.Delta);
                break;
            case AssistantToolCallEvent tc:
                AddToolCall(tc.ToolCall);
                break;
            case ToolExecutionEndEvent te:
                CompleteTool(te.ToolCall, te.Result);
                break;
            case TurnEndEvent:
                FinishStreaming();
                break;
            case HarnessErrorEvent he:
                FinishStreaming();
                AddError(he.Message);
                break;
        }
    }

    public void AddUserMessage(string text)
    {
        FinishStreaming();
        Add(new Group(new Markup("[primary]You[/]"), new XenoAtom.Terminal.UI.Controls.TextBlock(text).Wrap(true))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start));
    }

    public void AddError(string message)
    {
        FinishStreaming();
        Add(new Markup($"[red]✗ {ToolCardRenderer.Escape(message)}[/]") { Wrap = true });
    }

    private void AppendDelta(string delta)
    {
        if (_streamControl is null)
        {
            _streamText = new StringBuilder();
            _streamControl = new MarkdownControl(string.Empty)
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Start,
                Options = MarkdownRenderOptions.Default with
                {
                    MaxCodeBlockHeight = 10,
                    WrapText = true,
                },
            };
            Add(_streamControl);
        }

        _streamText!.Append(delta);
        _streamControl.Markdown = _streamText.ToString();
    }

    private void AddToolCall(ToolCall call)
    {
        FinishStreaming();
        var title = new Markup($"[primary]{ToolCardRenderer.Escape(ToolCardRenderer.FormatInvocation(call))}[/]");
        var body = new Markup("[dim]…[/]") { Wrap = false };
        var group = new Group(title, body)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start);
        Add(group);
        _toolCards[call.Id] = new ToolCard(call, title, body);
    }

    private void CompleteTool(ToolCall call, ToolResult result)
    {
        if (!_toolCards.TryGetValue(call.Id, out var card))
        {
            AddToolCall(call);
            card = _toolCards[call.Id];
        }

        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        var invocation = ToolCardRenderer.Escape(ToolCardRenderer.FormatInvocation(call));
        var summary = ToolCardRenderer.Escape(ToolCardRenderer.FormatSummary(call.Name, result));
        card.Title.Text = $"{status} [primary]{invocation}[/] [dim]· {summary}[/]";
        card.Body.Text = ToolCardRenderer.FormatResultBody(call.Name, result);
    }

    private void FinishStreaming()
    {
        _streamText = null;
        _streamControl = null;
    }

    private void Add(Visual content) => _flow.Items.Add(new DocumentFlowItem
    {
        Content = new FlowDocument().Add(content),
        Alignment = DocumentFlowAlignment.Stretch,
    });

    private sealed record ToolCard(ToolCall Call, Markup Title, Markup Body);
}
