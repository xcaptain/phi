using System.Text.Json.Nodes;

namespace PhiAgent;

public sealed record ToolResult(
    IReadOnlyList<ContentBlock> Content,
    JsonNode? Details = null,
    bool IsError = false)
{
    public string Text => string.Concat(
        Content.OfType<TextBlock>().Select(b => b.Text));
}
