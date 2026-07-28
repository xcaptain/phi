using PhiAgent;

namespace PhiCoding.Tui;

/// <summary>
/// Display-state model for the TUI (tau's TuiState equivalent): the ordered
/// list of <see cref="ChatItem"/>s plus run/turn/usage status. Mutated only
/// through <see cref="TuiEventAdapter"/> (agent events) and
/// <see cref="AddUserMessage"/> (user input). Raises <see cref="Changed"/>
/// after every mutation; views rebuild from <see cref="Items"/>.
/// </summary>
public sealed class TuiState
{
    private readonly List<ChatItem> _items = [];
    private ChatItem? _streamingAssistant;

    public IReadOnlyList<ChatItem> Items => _items;

    public bool IsRunning { get; private set; }

    public int CurrentTurn { get; private set; }

    public Usage LastUsage { get; private set; } = new();

    public event Action? Changed;

    public ChatItem AddUserMessage(string text)
    {
        FinishAssistant();
        var item = new ChatItem(ChatItemKind.User, TranscriptStyle.User);
        item.Text.Append(text);
        _items.Add(item);
        NotifyChanged();
        return item;
    }

    internal void BeginTurn(int turn)
    {
        CurrentTurn = turn;
        IsRunning = true;
        NotifyChanged();
    }

    internal void AppendAssistantDelta(string delta)
    {
        EnsureStreamingAssistant().Text.Append(delta);
        NotifyChanged();
    }

    internal void AddToolCall(ToolCall call)
    {
        FinishAssistant();
        _items.Add(new ChatItem(ChatItemKind.Tool, TranscriptStyle.ToolCall)
        {
            ToolCallId = call.Id,
            StyledLines = ToolBlockRenderer.RenderInvocationLines(call),
        });
        NotifyChanged();
    }

    internal void CompleteTool(ToolCall call, ToolResult result)
    {
        var item = _items.LastOrDefault(i => i.ToolCallId == call.Id);
        if (item is null)
        {
            AddToolCall(call);
            item = _items[^1];
        }

        item.StyledLines ??= [];
        item.StyledLines.AddRange(ToolBlockRenderer.RenderResultLines(call.Name, result));
        item.IsError = result.IsError;
        NotifyChanged();
    }

    internal void EndTurn(AssistantMessage finalMessage)
    {
        FinishAssistant();
        IsRunning = false;
        LastUsage = finalMessage.Usage;
        NotifyChanged();
    }

    internal void AddError(string message)
    {
        FinishAssistant();
        IsRunning = false;
        var item = new ChatItem(ChatItemKind.Error, TranscriptStyle.Error);
        item.Text.Append("[error] ").Append(message);
        _items.Add(item);
        NotifyChanged();
    }

    private ChatItem EnsureStreamingAssistant()
    {
        if (_streamingAssistant is not null) return _streamingAssistant;
        var item = new ChatItem(ChatItemKind.Assistant, TranscriptStyle.Assistant);
        _items.Add(item);
        _streamingAssistant = item;
        return item;
    }

    private void FinishAssistant() => _streamingAssistant = null;

    private void NotifyChanged() => Changed?.Invoke();
}
