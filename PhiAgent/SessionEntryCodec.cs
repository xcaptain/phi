using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhiAgent;

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
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = false,
        WriteIndented = false,
    };

    public static string Serialize(SessionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Use the abstract base as the declared type so the polymorphic
        // contract fires and the `kind` discriminator is written. Serializing
        // via the concrete type would emit a self-typed object with no
        // discriminator and deserialize would fail on the next load.
        return JsonSerializer.Serialize<SessionEntry>(entry, Options) + "\n";
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
            "user" => JsonSerializer.Deserialize<UserSessionEntry>(line, Options)
                ?? throw new InvalidDataException("Failed to deserialize user entry"),
            "assistant" => JsonSerializer.Deserialize<AssistantSessionEntry>(line, Options)
                ?? throw new InvalidDataException("Failed to deserialize assistant entry"),
            "toolResult" => JsonSerializer.Deserialize<ToolResultSessionEntry>(line, Options)
                ?? throw new InvalidDataException("Failed to deserialize toolResult entry"),
            _ => throw new InvalidDataException(
                $"Unknown session entry kind '{kind}': {line}"),
        };
    }
}
