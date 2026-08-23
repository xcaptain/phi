using Phi.Agent;

namespace Phi.Extensions.CodingPack.Tools.Details;

/// <summary>
/// Metadata about a <c>read</c> tool invocation: where the slice started,
/// how many lines were returned, and how big the source file is.
/// Persisted in <see cref="ToolResult.Details"/> so the status bar / tool
/// card renderer can show "lines N-M of T" without re-reading the file.
/// </summary>
public sealed record ReadDetails(
    string Path,
    int Offset,
    int Limit,
    int LineCount,
    int TotalLineCount,
    int ByteCount);
