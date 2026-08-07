namespace PhiCoding.ToolCards;

/// <summary>
/// Per-tool display metadata shared between the TUI and the desktop UI. Each
/// tool picks one descriptor (Title, IconKey, Kind); each UI renders its own
/// card from the descriptor. Tool <em>cards</em> (the visual layouts) are
/// still UI-specific — only the static metadata is shared.
/// </summary>
public enum ToolKind
{
    /// <summary>Reads the contents of a file.</summary>
    Read,
    /// <summary>Writes / creates a file.</summary>
    Write,
    /// <summary>Edits a file by replacing strings.</summary>
    Edit,
    /// <summary>Runs a shell command.</summary>
    Bash,
    /// <summary>Fallback for any other tool name (MCP tools, custom providers).</summary>
    Generic,
}

/// <summary>
/// Static metadata for one tool. Used as a lookup key by each UI's tool-card
/// registry. The metadata answers "what kind of tool is this and how should
/// we label it" — not "how do we render the card".
/// </summary>
/// <param name="Kind">Logical category, drives the card shape (one-line vs. title+body).</param>
/// <param name="Title">Short label (e.g. <c>"read"</c>, <c>"edit"</c>) for chip rendering.</param>
/// <param name="IconKey">
/// Free-form icon identifier. Each UI maps this to its own icon set (the TUI
/// uses emoji glyphs; Desk uses MewUI <c>PathShape</c> data or text labels).
/// Unrecognized keys degrade gracefully to plain text.
/// </param>
public sealed record ToolDescriptor(ToolKind Kind, string Title, string IconKey);
