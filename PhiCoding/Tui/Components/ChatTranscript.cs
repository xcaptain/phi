using System.Text;
using PhiAgent;
using PhiCoding.Resources;
using PhiCoding.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;

namespace PhiCoding.Tui.Components;

/// <summary>
/// The scrolling conversation view: a <see cref="DocumentFlow"/> of chat cards.
/// Assistant text streams into a per-turn <see cref="MarkdownControl"/>;
/// tool calls render via <see cref="IToolCard"/> implementations resolved by
/// <see cref="ToolCardRegistry"/>; reasoning streams into its own dim-styled
/// block ahead of the assistant text so the user can follow the model's thinking.
/// </summary>
public sealed class ChatTranscript
{
    private enum StreamMode { None, Thinking, Text }

    private readonly DocumentFlow _flow;
    private readonly Dictionary<string, IToolCard> _toolCards = [];
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
            ItemPadding = new Thickness(2, 0, 2, 0),
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

        // Session-level errors are routed to the status bar by PhiTuiApp's
        // binding; the transcript only keeps persistent-error markers, added
        // explicitly via AddPersistentError.
    }

    private void AppendVisualForMessage(IAgentMessage msg)
    {
        switch (msg)
        {
            case UserMessage u when u.Text.StartsWith(
                    ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal):
                // Hidden infrastructure: a compaction summary rides along as
                // a UserMessage. Render it as a subtle divider so users see
                // the boundary, not a fake "[You]" turn.
                AddCompactionDivider(u.Text[ContextWindow.CompactionSummaryPrefix.Length..]);
                break;
            case UserMessage u:
                AddUserMessage(u.Text);
                break;
            case AssistantMessage a:
                var thinking = a.ThinkingText;
                if (thinking.Length > 0)
                {
                    var durMs = a.Content.OfType<ThinkingBlock>()
                        .FirstOrDefault()?.DurationMs;
                    AddThinkingVisual(thinking, durMs);
                }

                // Same pending card as the streaming path.
                foreach (var tc in a.ToolCalls)
                    AddToolCall(tc);

                var mdText = a.Text;
                if (mdText.Length > 0)
                {
                    // MarkdownControl has no Padding (that's Group-only);
                    // Visual.Margin adds the same left/right spacing for a
                    // borderless control and keeps text off the window edge.
                    Add(new MarkdownControl(mdText)
                    {
                        Margin = new Thickness(2, 0, 2, 0),
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
                CompleteToolCallFromMessage(tr);
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
                // Transient / persistent routing happens in PhiTuiApp's
                // status-bar binding, not here — the transcript only needs
                // the persistent-error marker, which is added by the router.
                break;
        }
    }

    // ──────── Shared helpers (streaming + bulk) ────────

    /// <summary>
    /// Adds a finished thinking group — same layout as the streaming path's
    /// <see cref="StartThinkingStream"/>/<see cref="EndThinkingStream"/>.
    /// If <paramref name="durationMs"/> is available the title includes a
    /// duration label (e.g. <c>"💭 Thought 2.3s"</c>), matching the
    /// streaming path exactly.
    /// </summary>
    private void AddThinkingVisual(string text, double? durationMs = null)
    {
        var dur = durationMs is not null
            ? $" {FormatThinkingDuration(TimeSpan.FromMilliseconds(durationMs.Value))}"
            : "";
        var title = new Markup($"[dim]💭 Thought{dur}[/]") { Wrap = false };
        var content = new Markup(FormatThinkingText(text)) { Wrap = true, IsSelectable = true };
        Add(new Group(title, content)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start)
            .Padding(1));
    }

    /// <summary>
    /// Resolves the matching <see cref="IToolCard"/> from
    /// <see cref="_toolCards"/> and completes it. If no card is registered
    /// yet (resume edge), synthesizes one and completes immediately.
    /// </summary>
    private void CompleteToolCallFromMessage(ToolResultMessage tr)
    {
        var stubResult = new ToolResult(tr.Content, tr.Details, tr.IsError);

        if (!_toolCards.TryGetValue(tr.ToolCallId, out var card))
        {
            // Resume edge: no prior streaming event produced a card, so
            // synthesize one (no original arguments) and complete in place.
            AddToolCall(new ToolCall(tr.ToolCallId, tr.ToolName));
            card = _toolCards[tr.ToolCallId];
        }

        card.Complete(stubResult);
    }

    // ──────── User-facing helpers ────────

    public void AddUserMessage(string text)
    {
        FinishStreaming();
        // A skill invocation (<skill> block) renders as a collapsible
        // [skill] card instead of a plain "You" bubble with raw XML text.
        if (SkillInvocation.TryParse(text, out var block))
        {
            Add(new SkillInvocationCard(block!).Visual);
            return;
        }
        Add(new Group(new Markup("[primary]You[/]"), new XenoAtom.Terminal.UI.Controls.TextBlock(text).Wrap(true))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start));
    }

    /// <summary>
    /// Adds a persistent-error marker line to the transcript. Persistent
    /// errors also occupy the status bar (via <see cref="PhiStatusBar.ShowError"/>)
    /// but only the transcript record survives <see cref="PhiStatusBar.ClearError"/>
    /// on the next state change.
    /// </summary>
    public void AddPersistentError(string message)
    {
        FinishStreaming();
        Add(new Markup($"[red]✗ {ToolCardBase.Escape(message)}[/]") { Wrap = true });
    }

    /// <summary>
    /// Adds an informational message to the transcript (neutral color, no
    /// status glyph). Used for non-error feedback such as "no sessions in
    /// the last 7 days" when the user invokes a UI action.
    /// </summary>
    public void AddInfo(string message)
    {
        FinishStreaming();
        Add(new Markup($"[dim]{ToolCardBase.Escape(message)}[/]") { Wrap = true });
    }

    /// <summary>
    /// Renders a compaction summary as a dim divider instead of a fake user
    /// turn. The summary is rendered as a short first line so the user can
    /// see the boundary and what was kept.
    /// </summary>
    public void AddCompactionDivider(string summary)
    {
        FinishStreaming();
        var firstLine = summary.Split('\n').FirstOrDefault() ?? "";
        var display = firstLine.Length > 120 ? firstLine[..117] + "…" : firstLine;
        Add(new Markup($"[dim]⋯ compacted earlier context — {ToolCardBase.Escape(display)} ⋯[/]")
        {
            Wrap = true,
        });
    }

    // ──────── Streaming (thinking) ────────

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

    // ──────── Streaming (text) ────────

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
                Margin = new Thickness(2, 0, 2, 0),
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

    // ──────── Tool cards ────────

    private void AddToolCall(ToolCall call)
    {
        FinishStreaming();
        var card = ToolCardRegistry.For(call.Name);
        card.ShowPending(call);
        _toolCards[call.Id] = card;
        Add(card.Visual);
    }

    private void CompleteTool(ToolCall call, ToolResult result)
    {
        if (!_toolCards.TryGetValue(call.Id, out var card))
        {
            AddToolCall(call);
            card = _toolCards[call.Id];
        }
        card.Complete(result);
    }

    // ──────── Bulk rebuild ────────

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
            AppendVisualForMessage(msg);
    }

    private void FinishStreaming()
    {
        _streamMode = StreamMode.None;
        _streamText = null;
        _streamControl = null;
        _thinkingTitleMarkup = null;
        _thinkingMarkup = null;
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
        return string.Join('\n', lines.Select(l => $"[dim]{ToolCardBase.Escape(l)}[/]"));
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
}
