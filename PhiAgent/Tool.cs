using System.Text.Json.Nodes;

namespace PhiAgent;

public sealed record Tool(
    string Name,
    string Description,
    JsonObject Parameters);