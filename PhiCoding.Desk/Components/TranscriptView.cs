using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiAgent;
using PhiCoding.Chat;
using PhiCoding.Desk.Components.ToolCards;
using PhiCoding.Resources;
using TextBlock = Aprillz.MewUI.Controls.TextBlock;

namespace PhiCoding.Desk.Components;

/// <summary>
/// The conversation view: a scrolling <see cref="ScrollViewer"/> around
/// a <see cref="StackPanel"/> that DIFFs the projector's
/// <see cref="ChatLine"/>s against its existing children. Stable
/// <see cref="ChatLine.Id"/>s drive the diff; new Ids add a fresh
/// element, existing Ids patch the existing element in place (text stream
/// extends, tool call completes, etc.).
/// <para>
/// The element actually added to the panel is the real control (a
/// <see cref="Border"/> bubble / <see cref="Label"/>); the per-line handle
/// merely carries the live sub-controls for in-place updates.
/// </para>
/// </summary>
public sealed class TranscriptView
{
    private readonly Dictionary<string, LineHandle> _visualsByLineId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDeskToolCard> _toolCards = new(StringComparer.Ordinal);
    private readonly StackPanel _panel;

    /// <summary>The scroll container that holds the chat history.</summary>
    public FrameworkElement Root { get; }

    public TranscriptView()
    {
        _panel = new StackPanel()
            .Orientation(Aprillz.MewUI.Orientation.Vertical)
            .Spacing(8)
            .Padding(12, 8);
        Root = new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Content(_panel);
    }

    /// <summary>Number of rendered line elements (tests).</summary>
    internal int LineCount => _panel.Count;

    /// <summary>Rendered line element at <paramref name="index"/> (tests).</summary>
    internal FrameworkElement LineAt(int index) => (FrameworkElement)_panel[index];

    /// <summary>The projector-assigned line Ids currently rendered (tests).</summary>
    internal IReadOnlyCollection<string> LineIds => _visualsByLineId.Keys;

    /// <summary>
    /// Subscribes to the projector. The initial projection is rendered
    /// synchronously; subsequent updates arrive through
    /// <see cref="ChatTranscriptProjector.Changed"/>.
    /// </summary>
    public void Bind(ChatTranscriptProjector projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        projector.Changed += OnProjectorChanged;
        OnProjectorChanged(projector.Current);
    }

    private void OnProjectorChanged(IReadOnlyList<ChatLine> lines)
    {
        foreach (var line in lines)
        {
            if (_visualsByLineId.TryGetValue(line.Id, out var existing))
                UpdateLine(existing, line);
            else
                CreateAndAdd(line);
        }
    }

    private void CreateAndAdd(ChatLine line)
    {
        var handle = CreateHandle(line);
        _visualsByLineId[line.Id] = handle;
        _panel.Add(handle.Root);
    }

    private static void UpdateLine(LineHandle handle, ChatLine line)
    {
        switch (handle, line)
        {
            case (AssistantTextHandle t, AssistantTextLine a):
                t.UpdateText(a.Text);
                break;
            case (ThinkingHandle t, ThinkingLine th):
                t.UpdateText(th.Text, th.Duration);
                break;
            case (ToolCallHandle t, ToolCallLine tc):
                if (t.LastResultState != tc.ResultState)
                {
                    var contentBlocks = tc.ResultText is { Length: > 0 }
                        ? new ContentBlock[] { new PhiAgent.TextBlock(tc.ResultText) }
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

    // ──────── Handle creation ────────

    private LineHandle CreateHandle(ChatLine line) => line switch
    {
        UserTextLine u => new StaticHandle(CreateUserTextBubble(u)),
        SkillInvocationLine s => new StaticHandle(CreateSkillInvocationBubble(s)),
        CompactionDividerLine c => new StaticHandle(CreateCompactionDivider(c)),
        ThinkingLine t => CreateThinkingHandle(t),
        AssistantTextLine a => CreateAssistantTextHandle(a),
        ToolCallLine tc => CreateToolCallHandle(tc),
        PersistentErrorLine e => new StaticHandle(CreatePersistentErrorBubble(e)),
        _ => new StaticHandle(new Label().Text($"[unknown line: {line.GetType().Name}]")),
    };

    private static Border CreateUserTextBubble(UserTextLine line)
        => new Border()
            .Padding(14)
            .CornerRadius(10)
            .WithTheme((t, b) =>
            {
                b.Background(t.Palette.ContainerBackground);
                b.BorderBrush(t.Palette.ControlBorder);
            })
            .BorderThickness(1)
            .Child(
                new StackPanel()
                    .Orientation(Aprillz.MewUI.Orientation.Vertical)
                    .Spacing(4)
                    .Children(
                        new Label().Text("You").SemiBold().FontSize(12),
                        new TextBlock().Text(line.Text).TextWrapping(TextWrapping.Wrap)));

    private static Border CreateSkillInvocationBubble(SkillInvocationLine line)
    {
        var block = new SkillBlock(line.SkillName, "", line.Body, line.TrailingPrompt);
        var header = new Label()
            .Text($"[skill] {block.Name}")
            .SemiBold();
        var body = new Label()
            .Text(block.Content)
            .TextWrapping(TextWrapping.Wrap)
            .FontFamily("Consolas")
            .WithTheme((t, c) => c.Foreground(t.Palette.PlaceholderText));
        var expander = new Expander()
            .Header(header)
            .Content(body);
        return new Border()
            .Padding(14)
            .CornerRadius(10)
            .WithTheme((t, b) =>
            {
                b.Background(t.Palette.ContainerBackground);
                b.BorderBrush(t.Palette.ControlBorder);
            })
            .BorderThickness(1)
            .Child(expander);
    }

    private static Border CreateCompactionDivider(CompactionDividerLine line)
    {
        var display = line.SummaryLine.Length > 120
            ? line.SummaryLine[..117] + "…"
            : line.SummaryLine;
        return new Border()
            .Padding(0, 4)
            .Child(new Label()
                .Text($"⋯ compacted earlier context — {display} ⋯")
                .TextWrapping(TextWrapping.Wrap)
                .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t))));
    }

    private static ThinkingHandle CreateThinkingHandle(ThinkingLine line)
    {
        var title = new ObservableValue<string>("💭 Thinking…");
        var bodyText = new ObservableValue<string>(line.Text);
        var titleLabel = new Label()
            .BindText(title)
            .SemiBold()
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));
        var bodyLabel = new TextBlock()
            .BindText(bodyText)
            .TextWrapping(TextWrapping.Wrap)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));
        var bubble = new Border()
            .Padding(14)
            .CornerRadius(10)
            .WithTheme((t, b) =>
            {
                b.Background(t.Palette.ContainerBackground);
                b.BorderBrush(t.Palette.ControlBorder);
            })
            .BorderThickness(1)
            .Child(
                new StackPanel()
                    .Orientation(Aprillz.MewUI.Orientation.Vertical)
                    .Spacing(4)
                    .Children(titleLabel, bodyLabel));
        return new ThinkingHandle(title, bodyText, bubble);
    }

    private static AssistantTextHandle CreateAssistantTextHandle(AssistantTextLine line)
    {
        var bodyLabel = new TextBlock()
            .Text(line.Text)
            .TextWrapping(TextWrapping.Wrap);
        var bubble = new Border()
            .Padding(14)
            .CornerRadius(10)
            .WithTheme((t, b) =>
            {
                b.Background(t.Palette.ContainerBackground);
            })
            .BorderThickness(0)
            .Child(bodyLabel);
        return new AssistantTextHandle(bodyLabel, bubble);
    }

    private ToolCallHandle CreateToolCallHandle(ToolCallLine line)
    {
        var card = DeskToolCardRegistry.For(line.ToolName);
        System.Text.Json.Nodes.JsonNode? args = null;
        if (!string.IsNullOrEmpty(line.ArgumentsJson) && line.ArgumentsJson != "{}")
            args = System.Text.Json.Nodes.JsonNode.Parse(line.ArgumentsJson);
        var stubCall = new ToolCall(line.ToolCallId, line.ToolName)
        {
            Arguments = (args as System.Text.Json.Nodes.JsonObject) ?? [],
        };
        card.ShowPending(stubCall);
        _toolCards[line.ToolCallId] = card;
        return new ToolCallHandle(card, ToolResultState.Pending, card.Visual);
    }

    private static Border CreatePersistentErrorBubble(PersistentErrorLine line)
        => new Border()
            .Padding(8, 6)
            .CornerRadius(6)
            .WithTheme((t, b) => b.Background(DeskTheme.DangerBackground(t)))
            .BorderThickness(0)
            .Child(new Label()
                .Text($"✗ {line.Message}")
                .TextWrapping(TextWrapping.Wrap)
                .WithTheme((t, c) => c.Foreground(DeskTheme.Danger(t))));

    // ──────── Per-line handles ────────

    /// <summary>Per-line update handle. <see cref="Root"/> is the element in
    /// the panel; the typed subtypes carry the live sub-controls.</summary>
    private abstract class LineHandle
    {
        public abstract FrameworkElement Root { get; }
    }

    /// <summary>Static (never-updated) line.</summary>
    private sealed class StaticHandle(FrameworkElement root) : LineHandle
    {
        public override FrameworkElement Root => root;
    }

    /// <summary>Assistant text line; the label is patched in place while
    /// the model streams.</summary>
    private sealed class AssistantTextHandle(TextBlock bodyLabel, FrameworkElement root) : LineHandle
    {
        public override FrameworkElement Root => root;
        public void UpdateText(string text) => bodyLabel.Text = text;
    }

    /// <summary>Thinking line; title + body update in-place.</summary>
    private sealed class ThinkingHandle(
        ObservableValue<string> title,
        ObservableValue<string> bodyText,
        FrameworkElement root) : LineHandle
    {
        public override FrameworkElement Root => root;
        public void UpdateText(string text, TimeSpan? duration)
        {
            bodyText.Value = text;
            title.Value = duration is { } d
                ? $"💭 Thought {FormatHelpers.FormatSeconds((int)d.TotalSeconds)}s"
                : "💭 Thinking…";
        }
    }

    /// <summary>Tool call line; the card completes in-place.</summary>
    private sealed class ToolCallHandle(
        IDeskToolCard card,
        ToolResultState lastResultState,
        FrameworkElement root) : LineHandle
    {
        public override FrameworkElement Root => root;
        public IDeskToolCard Card => card;
        public ToolResultState LastResultState { get; set; } = lastResultState;
    }
}
