using System.Text.Json;

namespace Phi.Agent;

/// <summary>
/// JSONL codec for <see cref="SessionEntry"/>. Each call to
/// <see cref="Serialize"/> emits a single line (no trailing newline beyond
/// the one appended at the end) so the output can be appended directly to
/// an open file. <see cref="Deserialize"/> is the inverse: parse one line
/// back to its concrete entry type via the polymorphic <c>kind</c>
/// discriminator.
/// </summary>
public static class SessionEntryCodec
{
    public static string Serialize(SessionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Use the abstract base as the declared type so the polymorphic
        // contract fires and the `kind` discriminator is written. Serializing
        // via the concrete type would emit a self-typed object with no
        // discriminator and deserialize would fail on the next load.
        return JsonSerializer.Serialize(entry, PhiAgentJsonContext.Default.SessionEntry) + "\n";
    }

    public static SessionEntry Deserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            throw new InvalidDataException("Cannot deserialize an empty line");

        using var doc = JsonDocument.Parse(line);
        if (!doc.RootElement.TryGetProperty("kind", out var kindProp) ||
            kindProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Session entry missing string 'kind' discriminator: {line}");
        }

        var kind = kindProp.GetString();
        return kind switch
        {
            "user" => JsonSerializer.Deserialize(line, PhiAgentJsonContext.Default.UserSessionEntry)
                ?? throw new InvalidDataException("Failed to deserialize user entry"),
            "assistant" => JsonSerializer.Deserialize(line, PhiAgentJsonContext.Default.AssistantSessionEntry)
                ?? throw new InvalidDataException("Failed to deserialize assistant entry"),
            "toolResult" => JsonSerializer.Deserialize(line, PhiAgentJsonContext.Default.ToolResultSessionEntry)
                ?? throw new InvalidDataException("Failed to deserialize toolResult entry"),
            "compaction" => JsonSerializer.Deserialize(line, PhiAgentJsonContext.Default.CompactionSessionEntry)
                ?? throw new InvalidDataException("Failed to deserialize compaction entry"),
            _ => throw new InvalidDataException(
                $"Unknown session entry kind '{kind}': {line}"),
        };
    }
}
