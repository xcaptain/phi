using System.Text;

namespace PhiCoding.Tui;

public enum ChatItemKind
{
    User,
    Assistant,
    Tool,
    Status,
    Error,
}

/// <summary>
/// One entry in the conversation transcript. Text-based items (user,
/// assistant, status, error) accumulate into <see cref="Text"/> and are
/// rendered with <see cref="DefaultStyle"/>; tool items carry pre-styled
/// <see cref="StyledLines"/> (invocation + result + colored diff).
/// </summary>
public sealed class ChatItem
{
    public ChatItem(ChatItemKind kind, TranscriptStyle defaultStyle)
    {
        Kind = kind;
        DefaultStyle = defaultStyle;
    }

    public ChatItemKind Kind { get; }

    public TranscriptStyle DefaultStyle { get; }

    /// <summary>Set for tool items so results can be matched to their invocation.</summary>
    public string? ToolCallId { get; init; }

    public bool IsError { get; set; }

    public StringBuilder Text { get; } = new();

    /// <summary>When set, takes precedence over <see cref="Text"/> for rendering.</summary>
    public List<TranscriptLine>? StyledLines { get; set; }
}
