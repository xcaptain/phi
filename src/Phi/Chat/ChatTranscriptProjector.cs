using Phi.Agent;
using Phi.Resources;

namespace Phi.Chat;

/// <summary>
/// Subscribes to an <see cref="ISession"/> and projects its activity into a
/// UI-agnostic ordered list of <see cref="ChatLine"/>. Both the TUI and the
/// desktop UI render the same list — projectors are the single source of
/// truth for "what is currently on the chat screen", renderers are the
/// single source of truth for "how to draw it".
/// <para>
/// Stable per-line <see cref="ChatLine.Id"/>s let renderers DIFF a new
/// projection against their previous visual tree: same Id → patch the
/// existing visual in place (e.g. extend an in-flight text stream), new Id
/// → add a new visual at the end, deleted Id → no-op (the projector never
/// deletes lines).
/// </para>
/// </summary>
public sealed class ChatTranscriptProjector : IDisposable
{
    private readonly ISession _session;
    private readonly IExtensionRenderers? _renderers;
    private readonly List<ChatLine> _lines = [];
    private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _toolCallIndexByToolCallId = new(StringComparer.Ordinal);

    private int _nextLineId;
    private int _renderedMessageCount;
    private bool _isStreaming;
    private string? _thinkingLineId;
    private string? _textLineId;

    /// <summary>
    /// The projector emits this event after each mutation. Renderers should
    /// subscribe once and walk the entire <see cref="Current"/> list to apply
    /// incremental updates; the projector never emits deltas, only snapshots.
    /// </summary>
    public event Action<IReadOnlyList<ChatLine>>? Changed;

    /// <summary>The current projection in line order. Stable Ids.</summary>
    public IReadOnlyList<ChatLine> Current => _lines;

    /// <summary>
    /// Extension-registered renderers (tool cards / transcript lines /
    /// descriptors), when the host loaded any. Null in a host with no
    /// extension runtime (persistence-only sessions, headless tests).
    /// The projector holds the reference for the chat components to
    /// consult; the projector itself only uses it to enrich projected
    /// lines.
    /// </summary>
    public IExtensionRenderers? Renderers => _renderers;

    public ChatTranscriptProjector(ISession session, IExtensionRenderers? renderers = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _renderers = renderers;
        _session.HarnessEvent += Apply;
        _session.StateChanged += OnStateChanged;
        OnStateChanged(_session.State);
    }

    /// <summary>
    /// Marks the next <see cref="ISession.StateChanged"/> as a resume edge:
    /// all messages are replayed from index 0 instead of from
    /// <c>_renderedMessageCount</c>. Call this before switching sessions
    /// (so the renderer rebuilds against the new session's history).
    /// </summary>
    public void ResetRenderedCount() => _renderedMessageCount = 0;

    /// <summary>
    /// Adds a user message line directly (the prompt input commits the user's
    /// text to the transcript before <see cref="ISession.SubmitPrompt"/> runs,
    /// so the bubble appears without waiting for the harness turn). The same
    /// dispatch used on resume is used here so <c>/skill:</c> blocks and
    /// compaction-prefixed messages render as the right <see cref="ChatLine"/>
    /// subtype in both paths.
    /// </summary>
    public void SubmitUserLine(string text)
    {
        AppendUserTextInternal(text);
        Notify();
    }

    /// <summary>
    /// Adds a persistent error marker. The caller (status router) is
    /// responsible for dedup; the projector just records every message it
    /// sees.
    /// </summary>
    public void SubmitPersistentError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        AddLine(new PersistentErrorLine(NewId("er"), message));
        Notify();
    }

    /// <summary>
    /// Adds a custom extension-submitted line. <paramref name="lineType"/>
    /// is the discriminator the host uses to look up a renderer (registered
    /// via <c>IPhiApi.RegisterTranscriptLineRenderer</c>); without one the
    /// host renders <paramref name="content"/> as a plain-text bubble.
    /// <paramref name="details"/> is opaque structured data for the
    /// registered renderer only.
    /// </summary>
    /// <remarks>
    /// <paramref name="id"/> is the extension-provided stable line id (used
    /// by renderers for DIFF). When empty the projector assigns one; pass a
    /// stable id when the extension wants to update the same logical line
    /// in place across multiple submissions.
    /// </remarks>
    public void SubmitCustomLine(
        string lineType,
        string? id,
        string content,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineType);
        ArgumentNullException.ThrowIfNull(content);
        var lineId = string.IsNullOrWhiteSpace(id) ? NewId("cu") : id;
        AddLine(new CustomLine(lineId, lineType, content, details));
        Notify();
    }

    /// <summary>
    /// Adds a custom-typed assistant message line (<c>IPhiApi.SubmitCustomMessage</c>).
    /// The message was already persisted + injected into the harness by the
    /// session; this only surfaces it for rendering via the registered
    /// message renderer (falling back to plain text).
    /// </summary>
    public void SubmitCustomMessageLine(
        string customType,
        string content,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customType);
        ArgumentNullException.ThrowIfNull(content);
        AddLine(new CustomMessageLine(NewId("cm"), customType, content, details));
        Notify();
    }

    /// <summary>
    /// Clears the projection and reloads it from <paramref name="messages"/>.
    /// Use after navigation when the new session's history should be
    /// rendered without first running a fresh turn.
    /// </summary>
    public void ClearAndLoad(IReadOnlyList<IAgentMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _lines.Clear();
        _indexById.Clear();
        _toolCallIndexByToolCallId.Clear();
        _thinkingLineId = null;
        _textLineId = null;
        _isStreaming = false;
        _nextLineId = 0;
        _renderedMessageCount = 0;
        foreach (var msg in messages)
            AppendMessage(msg);
        _renderedMessageCount = messages.Count;
        Notify();
    }

    // ──────── Session subscription ────────

    private void OnStateChanged(SessionState state)
    {
        // During streaming the harness emits events directly; replaying
        // would double-render every line. Only replay on the resume edge
        // (between turns or after navigation).
        if (!_isStreaming)
        {
            for (var i = _renderedMessageCount; i < state.Messages.Count; i++)
                AppendMessage(state.Messages[i]);
            _renderedMessageCount = state.Messages.Count;
        }
        Notify();
    }

    private void Apply(HarnessEvent ev)
    {
        switch (ev)
        {
            case TurnStartEvent:
                _isStreaming = true;
                // The harness is about to commit its own UserMessage; once
                // streaming ends the StateChanged replay path will be a no-op.
                _renderedMessageCount = 0;
                break;
            case TurnEndEvent:
                _isStreaming = false;
                _renderedMessageCount = int.MaxValue;
                FinishTextStream();
                FinishThinkingStream();
                break;
            case MessageStartEvent ms when ms.Message is AssistantMessage:
                // The agent loop emits MessageStart before any streaming
                // updates for an assistant message. Nothing to render yet
                // — the first MessageUpdateEvent will open the actual line.
                break;
            case MessageUpdateEvent upd when upd.Message is AssistantMessage:
                DispatchProviderEvent(upd.ProviderEvent);
                break;
            case MessageEndEvent me when me.Message is AssistantMessage:
                FinishTextStream();
                FinishThinkingStream();
                break;
            case ToolExecutionEndEvent te:
                CompleteTool(te.ToolCallId, te.ToolName, te.Result, te.IsError);
                break;
        }
        Notify();
    }

    private void DispatchProviderEvent(ProviderEvent ev)
    {
        switch (ev)
        {
            case TextDeltaEvent t:
                AppendTextDelta(t.Delta);
                break;
            case ThinkingDeltaEvent t:
                AppendThinkingDelta(t.Delta);
                break;
            case ThinkingEndEvent:
                EndThinkingStream();
                break;
            case ToolCallEvent tc:
                FinishTextStream();
                FinishThinkingStream();
                AddToolCall(tc.ToolCall);
                break;
            case AssistantDoneEvent:
                // Terminal signal — MessageEndEvent (handled by the outer
                // switch) finalizes the streaming state.
                break;
        }
    }

    // ──────── Message replay ────────

    private void AppendMessage(IAgentMessage msg)
    {
        switch (msg)
        {
            case UserMessage u:
                AppendUserTextInternal(u.Text);
                break;
            case AssistantMessage a:
                var thinking = a.ThinkingText;
                if (thinking.Length > 0)
                {
                    AddLine(new ThinkingLine(
                        NewId("th"),
                        thinking,
                        IsStreaming: false));
                }
                foreach (var tc in a.ToolCalls)
                    AddToolCall(tc, isStreaming: false);
                if (a.Text.Length > 0)
                {
                    AddLine(new AssistantTextLine(NewId("at"), a.Text, IsStreaming: false));
                }
                break;
            case ToolResultMessage tr:
                CompleteToolByToolCallId(tr.ToolCallId, tr);
                break;
            case CustomMessage cm:
                // Extension-injected custom message. The Details field is a
                // JsonNode on the message; re-expose the raw text + type so
                // the renderer dispatch (RegisterMessageRenderer) can use it.
                AddLine(new CustomMessageLine(NewId("cm"), cm.CustomType, cm.Text, null));
                break;
        }
    }

    private void AppendUserTextInternal(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (SkillInvocation.TryParse(text, out var block) && block is not null)
        {
            AddLine(new SkillInvocationLine(
                NewId("sk"),
                block.Name,
                block.Content,
                block.UserMessage));
            return;
        }
        if (text.StartsWith(ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal))
        {
            var firstLine = text.Split('\n').FirstOrDefault() ?? "";
            AddLine(new CompactionDividerLine(NewId("cd"), firstLine));
            return;
        }
        AddLine(new UserTextLine(NewId("u"), text));
    }

    // ──────── Streaming ────────

    private void StartThinkingStream()
    {
        _thinkingLineId = NewId("th");
        AddLine(new ThinkingLine(_thinkingLineId, "", IsStreaming: true));
    }

    private void AppendThinkingDelta(string delta)
    {
        // Lazy-open the thinking line on the first delta (mirrors the
        // canonicalizer: there is no separate ThinkingStartEvent). A
        // text delta arriving after a closed thinking block starts a new
        // text line via AppendTextDelta.
        if (_thinkingLineId is null)
        {
            FinishTextStream();
            StartThinkingStream();
        }
        if (!_indexById.TryGetValue(_thinkingLineId!, out var idx)) return;
        var old = (ThinkingLine)_lines[idx];
        _lines[idx] = old with { Text = old.Text + delta };
    }

    private void EndThinkingStream()
    {
        if (_thinkingLineId is null) return;
        if (!_indexById.TryGetValue(_thinkingLineId, out var idx)) return;
        var old = (ThinkingLine)_lines[idx];
        _lines[idx] = old with { IsStreaming = false };
    }

    private void FinishThinkingStream()
    {
        // Stream buffer is closed; the line stays visible. We only clear the
        // tracking handle so a subsequent text delta creates a new line.
        _thinkingLineId = null;
    }

    private void AppendTextDelta(string delta)
    {
        // If we have an active thinking stream, leave it open — the next
        // text delta that arrives *after* the thinking end-event closes it.
        if (_textLineId is null)
        {
            FinishThinkingStream();
            _textLineId = NewId("at");
            AddLine(new AssistantTextLine(_textLineId, "", IsStreaming: true));
        }
        if (!_indexById.TryGetValue(_textLineId, out var idx)) return;
        var old = (AssistantTextLine)_lines[idx];
        _lines[idx] = old with { Text = old.Text + delta };
    }

    private void FinishTextStream()
    {
        if (_textLineId is null) return;
        if (_indexById.TryGetValue(_textLineId, out var idx))
        {
            var old = (AssistantTextLine)_lines[idx];
            _lines[idx] = old with { IsStreaming = false };
        }
        _textLineId = null;
    }

    // ──────── Tool calls ────────

    private void AddToolCall(ToolCall call, bool isStreaming = true)
    {
        var id = NewId("tc");
        var line = new ToolCallLine(
            Id: id,
            ToolCallId: call.Id,
            ToolName: call.Name,
            // Sprint 4: an extension can override the display descriptor
            // (icon / title / kind) via IPhiApi.RegisterToolCard; fall back
            // to the built-in table otherwise.
            Descriptor: _renderers is { } r && r.TryGetToolDescriptor(call.Name, out var d)
                ? d
                : ToolDescriptors.For(call.Name),
            ArgumentsJson: SerializeArgs(call.Arguments),
            ResultState: ToolResultState.Pending,
            ResultText: null,
            DetailsJson: null);
        AddLine(line);
        _toolCallIndexByToolCallId[call.Id] = _lines.Count - 1;
    }

    private void CompleteTool(string toolCallId, string toolName, ToolResult result, bool isError)
    {
        CompleteToolByToolCallId(toolCallId, result, isError);
    }

    private void CompleteToolByToolCallId(string toolCallId, ToolResult result, bool isError)
    {
        if (!_toolCallIndexByToolCallId.TryGetValue(toolCallId, out var idx))
        {
            // Resume edge: no streaming event produced a card. Synthesize
            // one (no arguments) and complete in place.
            var stub = new ToolCall(toolCallId, "");
            AddToolCall(stub);
            if (!_toolCallIndexByToolCallId.TryGetValue(toolCallId, out idx))
                return;
        }
        var old = (ToolCallLine)_lines[idx];
        _lines[idx] = old with
        {
            ResultState = isError ? ToolResultState.Failed : ToolResultState.Completed,
            ResultText = result.Text,
            DetailsJson = result.Details?.ToJsonString(),
        };
    }

    private void CompleteToolByToolCallId(string toolCallId, ToolResultMessage tr)
    {
        // The harness keeps the tool name on the result message; the
        // projector's stub uses "" since the title is reconstructed by the
        // renderer from the descriptor.
        var result = new ToolResult(tr.Content, tr.Details, tr.IsError);
        CompleteToolByToolCallId(toolCallId, result, tr.IsError);
    }

    private static string SerializeArgs(System.Text.Json.Nodes.JsonNode? args) =>
        args?.ToJsonString() ?? "{}";

    // ──────── Internals ────────

    private string NewId(string prefix)
    {
        var id = $"{prefix}{_nextLineId}";
        _nextLineId++;
        return id;
    }

    private void AddLine(ChatLine line)
    {
        _lines.Add(line);
        _indexById[line.Id] = _lines.Count - 1;
    }

    private void Notify() => Changed?.Invoke(_lines);

    /// <summary>
    /// Unsubscribes from the session. The renderer (TUI/Desk) owns the
    /// projector and is responsible for disposing it when its chat page is
    /// torn down — leaving the projector alive keeps it subscribed to
    /// <see cref="ISession.HarnessEvent"/> on a session that's already been
    /// navigated away from.
    /// </summary>
    public void Dispose()
    {
        _session.HarnessEvent -= Apply;
        _session.StateChanged -= OnStateChanged;
    }
}
