using Phi.Agent;
using Phi.Chat;
using Phi.Extensions;
using Phi.Resources;
using Phi.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.Markdown;
using XenoAtom.Terminal.UI.Geometry;
using TextBlock = XenoAtom.Terminal.UI.Controls.TextBlock;

namespace Phi.Tui.Components;

/// <summary>
/// The conversation view: a scrolling <see cref="DocumentFlow"/> of chat cards
/// plus a single-line transient region for input-status messages (steering
/// queued while running, dialog feedback like "Connected to …"). The
/// transcript subscribes to a <see cref="ChatTranscriptProjector"/> that
/// owns the UI-agnostic projection of <see cref="ChatLine"/>s; this class
/// only maps lines into XenoAtom visuals and diffs them against the
/// existing <see cref="DocumentFlow"/>.
/// </summary>
public sealed class ChatTranscript : IDisposable
{
    private readonly DocumentFlow _flow;
    // _transient is initialized in the ctor body (not as a field
    // initializer) because its Markup ctor touches a XenoAtom visual
    // tree property setter, which VerifyAccess() guards. With a field
    // initializer the assignment runs on whatever thread allocates the
    // ChatTranscript — outside the dispatcher if the caller is on a
    // worker thread (e.g. an integration test that constructs the
    // transcript outside the UI thread). Initializing in the ctor body
    // keeps the visual mutation on the same thread as the rest of the
    // construction, which is the caller's responsibility (production
    // always constructs on the UI thread; tests construct synchronously
    // on the test thread without a TerminalApp bound, so VerifyAccess
    // returns true).
    private readonly Markup _transient;
    private TuiUiThread _uiThread;
    private ChatTranscriptProjector? _projector;

    // Per-line visual handles, keyed by the projector-assigned stable Id.
    // New Ids add a fresh visual; existing Ids are patched in place (e.g.
    // an in-flight text stream extends its MarkdownControl).
    private readonly Dictionary<string, LineVisual> _visualsByLineId = new(StringComparer.Ordinal);

    // Kept for the existing TUI reflection tests (e.g. ReadToolCardTests),
    // which look up cards by ToolCall.Id rather than line Id. The same
    // ReadToolCard instance lives in both dictionaries.
    private readonly Dictionary<string, IToolCard> _toolCards = new(StringComparer.Ordinal);

    public ChatTranscript(TuiUiThread? uiThread = null)
    {
        // Default to the no-op marshaller so unit tests stay synchronous;
        // production paths call Bind() with the real TerminalApp-bound
        // marshaller before any events flow.
        _uiThread = uiThread ?? TuiUiThread.None;
        _transient = new Markup("") { Wrap = true, Margin = new Thickness(2, 0, 2, 0), };
        _flow = new DocumentFlow
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            ItemPadding = new Thickness(2, 0, 2, 0),
            ItemSpacing = 1,
            FollowTail = true,
            MaxCapacity = 500,
        };
        _transient.IsVisible = false;

        Visual = new DockLayout()
            .Top(_flow)
            .Bottom(_transient)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
    }

    /// <summary>The full visual: conversation flow + transient region.</summary>
    public Visual Visual { get; }

    /// <summary>The scrolling conversation flow (chat history).</summary>
    public DocumentFlow Flow => _flow;

    /// <summary>The latest transient input-status message, or null.</summary>
    public string? TransientText { get; private set; }

    /// <summary>
    /// Shows a transient input-status message in the region just above the
    /// editor. Replaces any previous transient; stays until the next
    /// <see cref="ShowTransient"/> call.
    /// </summary>
    public void ShowTransient(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        TransientText = message;
        _transient.Text = $"[dim]{Escape(message)}[/]";
        _transient.IsVisible = true;
    }

    /// <summary>
    /// Binds to a session by constructing a projector that subscribes to
    /// the session's events. The initial projection is rendered
    /// synchronously; subsequent updates arrive through
    /// <see cref="ChatTranscriptProjector.Changed"/>.
    /// </summary>
    public void Bind(
        ISession session,
        IExtensionRenderers? renderers = null,
        TuiUiThread? uiThread = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _projector?.Dispose();
        // Allow the caller (StatusBarBinder) to upgrade the marshaller
        // from the synchronous test default (TuiUiThread.None) to the
        // real TerminalApp-bound one. We always read _uiThread inside
        // the marshalled handler, so swapping the field post-construction
        // is safe — the next projector event picks up the new dispatcher.
        if (uiThread is not null) _uiThread = uiThread;
        _projector = new ChatTranscriptProjector(session, renderers);
        // The projector fires Changed from whatever thread the harness
        // emits HarnessEvent on (the streaming provider's IO completion
        // thread for real LLMs, the calling thread for sync mocks).
        // XenoAtom's DocumentFlow / MarkdownControl mutations must happen
        // on the TerminalApp's UI thread, so we marshal the diff through
        // the dispatcher before touching any visual. TuiUiThread.None (the
        // default) keeps synchronous semantics for tests.
        _projector.Changed += OnProjectorChangedMarshalled;
        // Render the initial projection (resume edge) — this fires from
        // the constructor's subscription path on the calling thread, which
        // is always the UI thread in production (BuildCurrentPage runs on
        // it) and the test thread in unit tests.
        OnProjectorChanged(_projector.Current);
    }

    private void OnProjectorChangedMarshalled(IReadOnlyList<ChatLine> lines)
    {
        // Capture lines into a closure that runs on the UI thread. The
        // projector holds the same list reference between events, so the
        // closure must read its contents synchronously when the action
        // fires — that's why we pass `lines` by argument instead of
        // reaching for `_projector.Current` from inside the marshalled
        // lambda (which would risk a torn read if the projector mutated
        // it again on the worker thread between Post and execution).
        _uiThread.Post(() => OnProjectorChanged(lines));
    }

    /// <summary>
    /// Adds a user message line directly (PromptInput calls this before
    /// <see cref="ISession.SubmitPrompt"/> so the user bubble appears
    /// without waiting for the harness turn).
    /// </summary>
    public void AddUserMessage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _projector?.SubmitUserLine(text);
    }

    /// <summary>
    /// Adds a persistent error marker line. Routed by
    /// <see cref="Phi.Status.SessionStatusRouter"/>; dedup happens
    /// upstream so the projector sees one event per failure.
    /// </summary>
    public void AddPersistentError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _projector?.SubmitPersistentError(message);
    }

    /// <summary>
    /// Adds an extension-submitted custom line into the projector. The
    /// UI sink (<see cref="Phi.Extensions.Host.IUiSink"/>) routes
    /// <c>IPhiApi.SubmitTranscriptLine</c> here; the projector converts it
    /// into a <see cref="CustomLine"/> and the transcript renders it via
    /// whatever renderer the extension registered (falling back to a plain
    /// text bubble).
    /// </summary>
    public void SubmitCustomLine(Phi.Extensions.TranscriptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _projector?.SubmitCustomLine(line.Type, line.Id, line.Content, line.Details);
    }

    /// <summary>
    /// Adds an extension-injected custom assistant message into the
    /// projector. The sink routes <c>IPhiApi.SubmitCustomMessage</c> here;
    /// the transcript renders it via whatever message renderer the extension
    /// registered (falling back to a plain text bubble).
    /// </summary>
    public void SubmitCustomMessageLine(
        string customType,
        string content,
        IReadOnlyDictionary<string, object?>? details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customType);
        ArgumentNullException.ThrowIfNull(content);
        _projector?.SubmitCustomMessageLine(customType, content, details);
    }

    /// <summary>
    /// Clears the transcript and rebuilds it from a message list. Used when
    /// switching to a resumed session.
    /// </summary>
    public void ClearAndLoad(IReadOnlyList<IAgentMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        // Drop every existing visual handle and tool card before the
        // projector clears its line list — the new projection re-issues
        // Ids starting at 0, so stale handles would shadow the new ones.
        _visualsByLineId.Clear();
        _toolCards.Clear();
        _projector?.ClearAndLoad(messages);
    }

    /// <summary>
    /// Resets the projector's replay cursor so the next
    /// <see cref="ISession.StateChanged"/> event replays messages from
    /// index 0. Defensive — PromptInput calls this after
    /// <see cref="PromptInput.LoadSkillAsync"/> to defend against double
    /// rendering. Existing lines stay in the projection.
    /// </summary>
    public void ResetRenderedCount() => _projector?.ResetRenderedCount();

    /// <summary>
    /// Disposes the projector, unsubscribing from the bound session's
    /// events. The TUI owns the transcript and disposes it when its chat
    /// page is torn down so the projector stops receiving harness events
    /// from a session that's already been navigated away from.
    /// </summary>
    public void Dispose() => _projector?.Dispose();

    // ──────── Projector diff ────────

    private void OnProjectorChanged(IReadOnlyList<ChatLine> lines)
    {
        foreach (var line in lines)
        {
            if (_visualsByLineId.TryGetValue(line.Id, out var existing))
                UpdateVisual(existing, line);
            else
                CreateAndAdd(line);
        }
    }

    private void CreateAndAdd(ChatLine line)
    {
        var visual = CreateVisual(line);
        _visualsByLineId[line.Id] = visual;
        AddToFlow(visual.RootVisual);
    }

    private static void UpdateVisual(LineVisual visual, ChatLine line)
    {
        switch (visual, line)
        {
            case (AssistantTextVisual t, AssistantTextLine a):
                t.Control.Markdown = a.Text;
                break;
            case (ThinkingVisual t, ThinkingLine th):
                t.Body.Text = FormatThinkingText(th.Text);
                t.Title.Text = ThinkingTitleMarkup(th);
                break;
            case (ToolCallVisual t, ToolCallLine tc):
                if (t.LastResultState != tc.ResultState)
                {
                    var contentBlocks = tc.ResultText is { Length: > 0 }
                        ? [new Phi.Agent.TextBlock(tc.ResultText)]
                        : Array.Empty<ContentBlock>();
                    var details = string.IsNullOrEmpty(tc.DetailsJson)
                        ? null
                        : System.Text.Json.Nodes.JsonNode.Parse(tc.DetailsJson);
                    t.Card.Complete(new ToolResult(contentBlocks, details,
                        tc.ResultState == ToolResultState.Failed));
                    t.LastResultState = tc.ResultState;
                }
                break;
        }
    }

    // ──────── Visual creation ────────

    private LineVisual CreateVisual(ChatLine line) => line switch
    {
        UserTextLine u => CreateUserTextVisual(u),
        SkillInvocationLine s => CreateSkillInvocationVisual(s),
        CompactionDividerLine c => CreateCompactionDividerVisual(c),
        ThinkingLine t => CreateThinkingVisual(t),
        AssistantTextLine a => CreateAssistantTextVisual(a),
        ToolCallLine tc => CreateToolCallVisual(tc),
        PersistentErrorLine e => CreatePersistentErrorVisual(e),
        CustomLine cu => CreateCustomLineVisual(cu),
        CustomMessageLine cm => CreateCustomMessageVisual(cm),
        _ => throw new InvalidOperationException($"Unknown line type: {line.GetType()}"),
    };

    /// <summary>
    /// Renders an extension-injected custom assistant message. Dispatches by
    /// <see cref="CustomMessageLine.CustomType"/> to the renderer registered
    /// via <c>IPhiApi.RegisterMessageRenderer</c>; falls back to a plain
    /// text bubble when no renderer is registered.
    /// </summary>
    private StaticVisual CreateCustomMessageVisual(CustomMessageLine line)
    {
        if (_projector?.Renderers?.TryGetMessageRenderer(line.CustomType, out var r) == true
            && r is Phi.Extensions.Rendering.MessageRenderer renderer)
        {
            var fragment = renderer(line.CustomType, line.Content, line.Details);
            if (fragment is Visual visual)
                return new StaticVisual(visual);
            if (fragment is string text)
                return CreateCustomTextVisual(text);
        }
        return CreateCustomTextVisual(line.Content);
    }

    /// <summary>
    /// Renders an extension-submitted <see cref="CustomLine"/>. Dispatches
    /// by <see cref="CustomLine.LineType"/> to the renderer the extension
    /// registered via <c>IPhiApi.RegisterTranscriptLineRenderer</c>; the
    /// renderer returns a <see cref="Visual"/> which is wrapped directly.
    /// Without a registered renderer the line falls back to a plain text
    /// bubble of <see cref="CustomLine.Content"/>.
    /// </summary>
    private StaticVisual CreateCustomLineVisual(CustomLine line)
    {
        if (_projector?.Renderers?.TryGetTranscriptLineRenderer(line.LineType, out var r) == true
            && r is Phi.Extensions.Rendering.TranscriptLineRenderer renderer)
        {
            var dto = new TranscriptLine(line.LineType, line.Id, line.Content, line.Details);
            var fragment = renderer(dto, Expanded: false);
            if (fragment is Visual visual)
                return new StaticVisual(visual);
            if (fragment is string text)
                return CreateCustomTextVisual(text);
        }
        return CreateCustomTextVisual(line);
    }

    /// <summary>Fallback plain-text bubble for a <see cref="CustomLine"/> with no renderer.</summary>
    private static StaticVisual CreateCustomTextVisual(CustomLine line)
    {
        var markup = new Markup(Escape(line.Content)) { Wrap = true };
        return new StaticVisual(markup);
    }

    private static StaticVisual CreateCustomTextVisual(string text)
    {
        var markup = new Markup(Escape(text)) { Wrap = true };
        return new StaticVisual(markup);
    }

    private static StaticVisual CreateUserTextVisual(UserTextLine line)
    {
        var group = new Group(
                new Markup("[primary]You[/]"),
                new TextBlock(line.Text).Wrap(true))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start);
        return new StaticVisual(group);
    }

    private static StaticVisual CreateSkillInvocationVisual(SkillInvocationLine line)
    {
        // Reconstruct a SkillBlock so the existing SkillInvocationCard
        // receives the original record shape; the projector splits it
        // into fields for cross-UI rendering, but the TUI card wants
        // the original block.
        var reconstructed = new SkillBlock(line.SkillName, "", line.Body, line.TrailingPrompt);
        var card = new SkillInvocationCard(reconstructed);
        return new StaticVisual(card.Visual);
    }

    private static StaticVisual CreateCompactionDividerVisual(CompactionDividerLine line)
    {
        var firstLine = line.SummaryLine;
        var display = firstLine.Length > 120 ? firstLine[..117] + "…" : firstLine;
        var markup = new Markup($"[dim]⋯ compacted earlier context — {Escape(display)} ⋯[/]")
        {
            Wrap = true,
        };
        return new StaticVisual(markup);
    }

    private static ThinkingVisual CreateThinkingVisual(ThinkingLine line)
    {
        var title = new Markup(ThinkingTitleMarkup(line)) { Wrap = false };
        var body = new Markup(FormatThinkingText(line.Text)) { Wrap = true, IsSelectable = true };
        var group = new Group(title, body)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start)
            .Padding(1);
        return new ThinkingVisual(group, title, body);
    }

    private static AssistantTextVisual CreateAssistantTextVisual(AssistantTextLine line)
    {
        var control = new MarkdownControl(line.Text)
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
        return new AssistantTextVisual(control);
    }

    private ToolCallVisual CreateToolCallVisual(ToolCallLine line)
    {
        var card = ToolCardRegistry.For(line.ToolName, _projector?.Renderers);
        // The projector stores arguments as a JSON string so the line stays
        // serializable; the TUI card needs the original ToolCall object to
        // extract path/offset/limit/command/etc. for the title and body.
        // Deserialize back into a JsonNode so the existing ToolCardBase
        // helpers keep working unchanged.
        System.Text.Json.Nodes.JsonNode? args = null;
        if (!string.IsNullOrEmpty(line.ArgumentsJson) && line.ArgumentsJson != "{}")
            args = System.Text.Json.Nodes.JsonNode.Parse(line.ArgumentsJson);
        var stubCall = new ToolCall(line.ToolCallId, line.ToolName) { Arguments = (args as System.Text.Json.Nodes.JsonObject) ?? [] };
        card.ShowPending(stubCall);
        _toolCards[line.ToolCallId] = card;
        return new ToolCallVisual(card, ToolResultState.Pending);
    }

    private static StaticVisual CreatePersistentErrorVisual(PersistentErrorLine line)
    {
        var markup = new Markup($"[red]✗ {Escape(line.Message)}[/]") { Wrap = true };
        return new StaticVisual(markup);
    }

    private void AddToFlow(Visual content) => _flow.Items.Add(new DocumentFlowItem
    {
        Content = new FlowDocument().Add(content),
        Alignment = DocumentFlowAlignment.Stretch,
    });

    private static string ThinkingTitleMarkup(ThinkingLine line) =>
        line.IsStreaming
            ? "[dim]💭 Thinking…[/]"
            : "[dim]💭 Thought[/]";

    // ──────── Visual handles ────────

    /// <summary>Per-line visual handle. Subtypes carry the typed controls
    /// the diff needs to patch an existing visual in place.</summary>
    private abstract class LineVisual
    {
        public abstract Visual RootVisual { get; }
    }

    private sealed class StaticVisual(Visual visual) : LineVisual
    {
        public override Visual RootVisual => visual;
    }

    private sealed class AssistantTextVisual(MarkdownControl control) : LineVisual
    {
        public MarkdownControl Control => control;
        public override Visual RootVisual => control;
    }

    private sealed class ThinkingVisual(Group group, Markup title, Markup body) : LineVisual
    {
        public Markup Title => title;
        public Markup Body => body;
        public override Visual RootVisual => group;
    }

    private sealed class ToolCallVisual(IToolCard card, ToolResultState lastResultState) : LineVisual
    {
        public IToolCard Card => card;
        public ToolResultState LastResultState { get; set; } = lastResultState;
        public override Visual RootVisual => card.Visual;
    }

    // ──────── Static helpers (preserved for tests) ────────

    /// <summary>
    /// Renders raw reasoning text as dim ANSI markup, one [dim]…[/] wrapper
    /// per line. Bracket characters in the source are escaped so the markup
    /// parser doesn't choke on <c>[dim]</c>-like tokens the model might emit.
    /// </summary>
    public static string FormatThinkingText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Select(l => $"[dim]{Escape(l)}[/]"));
    }

    private static string Escape(string text) =>
        text.Replace("[", "\\[").Replace("]", "\\]");
}
