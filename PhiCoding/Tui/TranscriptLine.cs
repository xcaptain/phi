namespace PhiCoding.Tui;

/// <summary>Semantic style of one transcript line; the view maps it to colors.</summary>
public enum TranscriptStyle
{
    Default,
    User,
    Assistant,
    ToolCall,
    ToolOk,
    ToolError,
    ToolOutput,
    DiffAdded,
    DiffRemoved,
    DiffMeta,
    Status,
    Error,
}

/// <summary>One rendered line in the transcript (after per-item rendering, before wrapping).</summary>
public sealed record TranscriptLine(string Text, TranscriptStyle Style);
