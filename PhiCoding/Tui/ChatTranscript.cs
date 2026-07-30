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
/// tool result arrives. Reasoning streams into its own dim-styled block
/// ahead of the assistant text so the user can follow the model's thinking.
/// Mirrors the XenoAtom Playground sample.
/// </summary>
public sealed class ChatTranscript
{
    private enum StreamMode { None, Thinking, Text }

    private readonly DocumentFlow _flow;
    private readonly Dictionary<string, ToolCard> _toolCards = new();
    private StreamMode _streamMode = StreamMode.None;
    private StringBuilder? _streamText;
    private MarkdownControl? _streamControl;
    private Markup? _thinkingTitleMarkup;
    private Markup? _thinkingMarkup;
    private DateTime _thinkingStartTime;
    private int _renderedMessageCount;
    private bool _isStreaming;

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

    /// <summary>
    /// Binds this transcript to a <see cref="ISession"/>. Streaming events
    /// (thinking, text deltas, tool calls) go through <see cref="Apply"/>
    /// for incremental rendering. Session-level state changes (resume,
    /// errors) go through <see cref="OnSessionState"/> for bulk rendering.
    /// </summary>
    public void Bind(ISession session)
    {
        session.HarnessEvent += Apply;
        session.StateChanged += OnSessionState;
        OnSessionState(session.State);
    }

    /// <summary>
    /// Resets the rendered-message counter so the next
    /// <see cref="OnSessionState"/> pass renders everything. Call before
    /// switching sessions (resume).
    /// </summary>
    public void ResetRenderedCount() { _renderedMessageCount = 0; _isStreaming = false; }

    private void OnSessionState(SessionState state)
    {
        // During streaming the harness events (Apply) handle all rendering.
        // Only render messages from state when NOT streaming, i.e. on
        // resume / initial load.
        if (!_isStreaming)
        {
            for (var i = _renderedMessageCount; i < state.Messages.Count; i++)
                AppendVisualForMessage(state.Messages[i]);
            _renderedMessageCount = state.Messages.Count;
        }

        if (state.LastError is { Length: > 0 })
            AddError(state.LastError);
    }

    private void AppendVisualForMessage(IAgentMessage msg)
    {
        switch (msg)
        {
            case UserMessage u:
                AddUserMessage(u.Text);
                break;
            case AssistantMessage a:
                var thinking = a.ThinkingText;
                if (thinking.Length > 0)
                {
                    Add(new Markup(FormatThinkingText(thinking))
                    {
                        Wrap = true, IsSelectable = true,
                    });
                }
                var mdText = a.Text;
                if (mdText.Length > 0)
                {
                    Add(new MarkdownControl(mdText)
                    {
                        HorizontalAlignment = Align.Stretch,
                        VerticalAlignment = Align.Start,
                        Options = MarkdownRenderOptions.Default with
                        {
                            MaxCodeBlockHeight = 10,
                            WrapText = true,
                        },
                    });
                }
                break;
            case ToolResultMessage tr:
                var style = tr.IsError ? "red" : "dim";
                Add(new Markup($"[{style}]✗ tool {tr.ToolName}: {ToolCardRenderer.Escape(tr.Text)}[/]")
                {
                    Wrap = true,
                });
                break;
        }
    }

    public void Apply(HarnessEvent ev)
    {
        switch (ev)
        {
            case TurnStartEvent:
                _isStreaming = true;
                _renderedMessageCount = 0;
                break;
            case TurnEndEvent:
                FinishStreaming();
                _isStreaming = false;
                // After streaming, all messages are already rendered via
                // AddUserMessage + streaming events. Advance the counter
                // so OnSessionState doesn't re-render them.
                _renderedMessageCount = int.MaxValue;
                break;
            case AssistantThinkingStartEvent:
                StartThinkingStream();
                break;
            case AssistantThinkingDeltaEvent d:
                AppendThinkingDelta(d.Delta);
                break;
            case AssistantThinkingEndEvent:
                EndThinkingStream();
                break;
            case AssistantTextDeltaEvent d:
                AppendTextDelta(d.Delta);
                break;
            case AssistantToolCallEvent tc:
                FinishStreaming();
                AddToolCall(tc.ToolCall);
                break;
            case ToolExecutionEndEvent te:
                CompleteTool(te.ToolCall, te.Result);
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

    private void StartThinkingStream()
    {
        // Close any in-flight stream (text or a previous thinking block).
        FinishStreaming();

        _streamMode = StreamMode.Thinking;
        _streamText = new StringBuilder();
        _thinkingStartTime = DateTime.UtcNow;

        _thinkingTitleMarkup = new Markup("[dim]💭 Thinking…[/]") { Wrap = false };
        _thinkingMarkup = new Markup(string.Empty) { Wrap = true };
        Add(new Group(_thinkingTitleMarkup, _thinkingMarkup)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start));
    }

    private void AppendThinkingDelta(string delta)
    {
        if (_streamMode != StreamMode.Thinking) return;
        if (_streamText is null || _thinkingMarkup is null) return;

        _streamText.Append(delta);
        _thinkingMarkup.Text = FormatThinkingText(_streamText.ToString());
    }

    private void EndThinkingStream()
    {
        if (_thinkingTitleMarkup is null) return;

        var elapsed = DateTime.UtcNow - _thinkingStartTime;
        _thinkingTitleMarkup.Text = $"[dim]💭 Thought {FormatThinkingDuration(elapsed)}[/]";

        // The block stays visible — we only stop accumulating. Next event
        // (text delta, tool call, turn end) will close it via FinishStreaming.
    }

    private void AppendTextDelta(string delta)
    {
        // Close a still-open thinking stream, but NOT a text stream — text
        // deltas must accumulate into the same MarkdownControl or each delta
        // would render as its own DocumentFlowItem.
        if (_streamMode != StreamMode.Text)
        {
            FinishStreaming();
        }

        if (_streamControl is null)
        {
            _streamMode = StreamMode.Text;
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
        _streamMode = StreamMode.None;
        _streamText = null;
        _streamControl = null;
        _thinkingTitleMarkup = null;
        _thinkingMarkup = null;
    }

    /// <summary>
    /// Clears the transcript and rebuilds it from a message list. Used when
    /// switching to a resumed session (popup resume). Resets stream state
    /// so the next <see cref="Apply"/> starts fresh.
    /// </summary>
    public void ClearAndLoad(IReadOnlyList<IAgentMessage> messages)
    {
        FinishStreaming();
        _flow.Items.Clear();
        _toolCards.Clear();

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case UserMessage u:
                    AddUserMessage(u.Text);
                    break;
                case AssistantMessage a:
                    var thinking = a.ThinkingText;
                    if (thinking.Length > 0)
                    {
                        Add(new Markup(FormatThinkingText(thinking))
                        {
                            Wrap = true, IsSelectable = true,
                        });
                    }
                    var mdText = a.Text;
                    if (mdText.Length > 0)
                    {
                        Add(new MarkdownControl(mdText)
                        {
                            HorizontalAlignment = Align.Stretch,
                            VerticalAlignment = Align.Start,
                            Options = MarkdownRenderOptions.Default with
                            {
                                MaxCodeBlockHeight = 10,
                                WrapText = true,
                            },
                        });
                    }
                    break;
                case ToolResultMessage tr:
                    var style = tr.IsError ? "red" : "dim";
                    Add(new Markup($"[{style}]✗ tool {tr.ToolName}: {ToolCardRenderer.Escape(tr.Text)}[/]")
                    {
                        Wrap = true,
                    });
                    break;
            }
        }
    }

    private void Add(Visual content) => _flow.Items.Add(new DocumentFlowItem
    {
        Content = new FlowDocument().Add(content),
        Alignment = DocumentFlowAlignment.Stretch,
    });

    /// <summary>
    /// Renders raw reasoning text as dim ANSI markup, one [dim]…[/] wrapper
    /// per line. Bracket characters in the source are escaped so the markup
    /// parser doesn't choke on `[dim]`-like tokens the model might emit.
    /// </summary>
    internal static string FormatThinkingText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Select(l => $"[dim]{ToolCardRenderer.Escape(l)}[/]"));
    }

    /// <summary>
    /// Formats a thinking-block duration for the "Thought Xs" header.
    /// Sub-second → ms, sub-minute → one decimal seconds, otherwise m+s.
    /// </summary>
    internal static string FormatThinkingDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
            return $"{(int)elapsed.TotalMilliseconds}ms";
        if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F1}s";
        var minutes = (int)elapsed.TotalMinutes;
        var seconds = (int)(elapsed.TotalSeconds - minutes * 60);
        return $"{minutes}m{seconds}s";
    }

    private sealed record ToolCard(ToolCall Call, Markup Title, Markup Body);
}