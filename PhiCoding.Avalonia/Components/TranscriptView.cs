using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Markdown.Avalonia.Full;
using PhiAgent;
using PhiCoding.Avalonia.Components.ToolCards;
using PhiCoding.Chat;
using PhiCoding.Resources;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Components;

/// <summary>
/// The conversation view: a scrolling <see cref="ScrollViewer"/> around
/// a <see cref="StackPanel"/> that DIFFs the projector's
/// <see cref="ChatLine"/>s against its existing children. Stable
/// <see cref="ChatLine.Id"/>s drive the diff; new Ids add a fresh
/// element, existing Ids patch the existing element in place (text stream
/// extends, tool call completes, etc.).
/// <para>
/// Assistant text renders through Markdown.Avalonia's
/// <see cref="MarkdownScrollViewer"/> so code fences, lists, and headings
/// come out formatted instead of raw markdown source. Streaming updates
/// re-assign the <c>Markdown</c> property in place.
/// </para>
/// </summary>
public sealed class TranscriptView
{
    private readonly Dictionary<string, LineHandle> _visualsByLineId = new(StringComparer.Ordinal);
    private readonly StackPanel _panel;
    private readonly Action<Action> _dispatchToUi;

    /// <summary>The scroll container that holds the chat history.</summary>
    public Control Root { get; }

    public TranscriptView(Action<Action>? dispatchToUi = null)
    {
        _dispatchToUi = dispatchToUi ?? Dispatch;
        _panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 4),
        };
        Root = new ScrollViewer
        {
            // Document-style reading margins: generous horizontal padding on
            // both sides so lines don't run to the window edge; the vertical
            // padding keeps breathing room at top and bottom.
            Padding = new Thickness(48, 16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _panel,
        };
    }

    /// <summary>Number of rendered line elements (tests).</summary>
    internal int LineCount => _panel.Children.Count;

    /// <summary>Rendered line element at <paramref name="index"/> (tests).</summary>
    internal Control LineAt(int index) => _panel.Children[index];

    /// <summary>The projector-assigned line Ids currently rendered (tests).</summary>
    internal IReadOnlyCollection<string> LineIds => _visualsByLineId.Keys;

    /// <summary>
    /// Subscribes to the projector. The initial projection is rendered
    /// synchronously; subsequent updates arrive through
    /// <see cref="ChatTranscriptProjector.Changed"/> and are marshalled to
    /// the UI thread via the dispatcher.
    /// </summary>
    public void Bind(ChatTranscriptProjector projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        projector.Changed += lines => _dispatchToUi(() => OnProjectorChanged(lines));
        OnProjectorChanged(projector.Current);
    }

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
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
        _panel.Children.Add(handle.Root);
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

    private static LineHandle CreateHandle(ChatLine line) => line switch
    {
        UserTextLine u => new StaticHandle(CreateUserTextBubble(u)),
        SkillInvocationLine s => new StaticHandle(CreateSkillInvocationBubble(s)),
        CompactionDividerLine c => new StaticHandle(CreateCompactionDivider(c)),
        ThinkingLine t => CreateThinkingHandle(t),
        AssistantTextLine a => CreateAssistantTextHandle(a),
        ToolCallLine tc => CreateToolCallHandle(tc),
        PersistentErrorLine e => new StaticHandle(CreatePersistentErrorBubble(e)),
        _ => new StaticHandle(new TextBlock { Text = $"[unknown line: {line.GetType().Name}]" }),
    };

    private static Grid CreateUserTextBubble(UserTextLine line)
    {
        var bubble = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(10),
            Background = AvaloniaTheme.Accent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new SelectableTextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = AvaloniaTheme.AccentText,
            },
        };

        // Two-column grid: left 1/5 stays empty (pushes the bubble right),
        // right 4/5 caps the bubble width at 80% of the panel. The bubble
        // sizes to its content (HorizontalAlignment=Right) so short messages
        // stay narrow; long messages fill the 4* column and wrap.
        var wrapper = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("*,4*"),
        };
        Grid.SetColumn(bubble, 1);
        wrapper.Children.Add(bubble);
        return wrapper;
    }

    private static Border CreateSkillInvocationBubble(SkillInvocationLine line)
    {
        var block = new SkillBlock(line.SkillName, "", line.Body, line.TrailingPrompt);
        var header = new TextBlock
        {
            Text = $"[skill] {block.Name}",
            FontWeight = FontWeight.SemiBold,
        };
        var body = new TextBlock
        {
            Text = block.Content,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = AvaloniaTheme.TextSecondary,
        };
        var expander = new Expander { Header = header, Content = body };
        return new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(10),
            Background = AvaloniaTheme.ContainerBackground,
            BorderBrush = AvaloniaTheme.ControlBorder,
            BorderThickness = new Thickness(1),
            Child = expander,
        };
    }

    private static Border CreateCompactionDivider(CompactionDividerLine line)
    {
        var display = line.SummaryLine.Length > 120
            ? line.SummaryLine[..117] + "…"
            : line.SummaryLine;
        return new Border
        {
            Padding = new Thickness(0, 4),
            Child = new TextBlock
            {
                Text = $"⋯ compacted earlier context — {display} ⋯",
                TextWrapping = TextWrapping.Wrap,
                Foreground = AvaloniaTheme.TextSecondary,
            },
        };
    }

    private static ThinkingHandle CreateThinkingHandle(ThinkingLine line)
    {
        var titleLabel = new TextBlock
        {
            Foreground = AvaloniaTheme.TextSecondary,
        };
        UpdateThinkingTitle(titleLabel, line.Duration, line.IsStreaming);

        var bodyLabel = new TextBlock
        {
            Text = line.Text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = AvaloniaTheme.TextSecondary,
            Margin = new Thickness(20, 4, 0, 4),
        };

        var section = new CollapsibleSection(titleLabel, bodyLabel, startExpanded: false);
        return new ThinkingHandle(titleLabel, bodyLabel, section);
    }

    private static void UpdateThinkingTitle(TextBlock titleLabel, TimeSpan? duration, bool isStreaming)
    {
        titleLabel.Text = !isStreaming && duration is { } d
            ? $"💭 Thought {FormatSeconds((int)d.TotalSeconds)}"
            : "💭 Thinking…";
    }

    private static string FormatSeconds(int seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60.0:F1}m",
        _ => $"{seconds / 3600.0:F1}h",
    };

    private static AssistantTextHandle CreateAssistantTextHandle(AssistantTextLine line)
    {
        // MarkdownScrollViewer measures to content when unconstrained, so
        // it sits inline in the transcript without its own scrollbar. No
        // card wrapper: assistant text reads as part of the document flow
        // (vertical spacing comes from the parent StackPanel's Spacing=8).
        var markdown = new MarkdownScrollViewer
        {
            Markdown = line.Text,
        };
        return new AssistantTextHandle(markdown, markdown);
    }

    private static ToolCallHandle CreateToolCallHandle(ToolCallLine line)
    {
        var card = AvaloniaToolCardRegistry.For(line.ToolName);
        System.Text.Json.Nodes.JsonNode? args = null;
        if (!string.IsNullOrEmpty(line.ArgumentsJson) && line.ArgumentsJson != "{}")
            args = System.Text.Json.Nodes.JsonNode.Parse(line.ArgumentsJson);
        var stubCall = new ToolCall(line.ToolCallId, line.ToolName)
        {
            Arguments = (args as System.Text.Json.Nodes.JsonObject) ?? [],
        };
        card.ShowPending(stubCall);

        // Resume edge: the projector's replay path delivers ToolCallLine
        // already in its final ResultState (stream mode instead saw Pending
        // first and then Complete via UpdateLine). Mirror stream mode by
        // calling Complete with the synthetic ToolResult rebuilt from the
        // line's persisted ResultText + DetailsJson so the card's title /
        // body reflect the completed state, not the placeholder.
        if (line.ResultState != ToolResultState.Pending)
            card.Complete(BuildResultFromLine(line));

        return new ToolCallHandle(card, line.ResultState, card.Visual);
    }

    private static ToolResult BuildResultFromLine(ToolCallLine line)
    {
        var contentBlocks = !string.IsNullOrEmpty(line.ResultText)
            ? new ContentBlock[] { new PhiAgent.TextBlock(line.ResultText) }
            : Array.Empty<ContentBlock>();
        var details = string.IsNullOrEmpty(line.DetailsJson)
            ? null
            : System.Text.Json.Nodes.JsonNode.Parse(line.DetailsJson);
        return new ToolResult(contentBlocks, details, line.ResultState == ToolResultState.Failed);
    }

    private static Border CreatePersistentErrorBubble(PersistentErrorLine line)
        => new()
        {
            Padding = new Thickness(8, 6),
            CornerRadius = new CornerRadius(6),
            Background = AvaloniaTheme.DangerBackground,
            BorderThickness = new Thickness(0),
            Child = new TextBlock
            {
                Text = $"✗ {line.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = AvaloniaTheme.Danger,
            },
        };

    // ──────── Per-line handles ────────

    /// <summary>Per-line update handle. <see cref="Root"/> is the element in
    /// the panel; the typed subtypes carry the live sub-controls.</summary>
    private abstract class LineHandle
    {
        public abstract Control Root { get; }
    }

    /// <summary>Static (never-updated) line.</summary>
    private sealed class StaticHandle(Control root) : LineHandle
    {
        public override Control Root => root;
    }

    /// <summary>Assistant text line; the markdown is patched in place while
    /// the model streams.</summary>
    private sealed class AssistantTextHandle(MarkdownScrollViewer markdown, Control root) : LineHandle
    {
        public override Control Root => root;
        public void UpdateText(string text) => markdown.Markdown = text;
    }

    /// <summary>Thinking line; title + body update in-place.</summary>
    private sealed class ThinkingHandle(
        TextBlock titleLabel,
        TextBlock bodyLabel,
        Control root) : LineHandle
    {
        public override Control Root => root;
        public void UpdateText(string text, TimeSpan? duration)
        {
            bodyLabel.Text = text;
            UpdateThinkingTitle(titleLabel, duration, isStreaming: false);
        }
    }

    /// <summary>Tool call line; the card completes in-place.</summary>
    private sealed class ToolCallHandle(
        IAvaloniaToolCard card,
        ToolResultState lastResultState,
        Control root) : LineHandle
    {
        public override Control Root => root;
        public IAvaloniaToolCard Card => card;
        public ToolResultState LastResultState { get; set; } = lastResultState;
    }
}
