namespace PhiCoding;

/// <summary>
/// One row in the global session index. Lives in <c>index.jsonl</c>; the
/// <c>Id</c> doubles as the JSONL filename for the session's messages
/// (e.g. <c>a1b2c3d4e5f6.jsonl</c>). Per <c>tau_coding.session_manager</c>.
/// </summary>
public sealed record SessionRecord(
    string Id,
    string Cwd,
    string Model,
    string? Title,
    long CreatedAt,
    long UpdatedAt);
