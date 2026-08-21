using Phi.Prompts;

namespace Phi.Tools;

/// <summary>
/// Validates a set of <see cref="ToolContribution"/> produced by one or more
/// providers. The first version only rejects duplicate names; future
/// versions can add namespace-prefix validation for MCP tools.
/// </summary>
public static class ToolComposer
{
    /// <summary>
    /// Returns the input contributions in the same order, after rejecting
    /// duplicate tool names. Throws <see cref="InvalidOperationException"/>
    /// with the offending name on conflict.
    /// </summary>
    public static ValueTask<IReadOnlyList<ToolContribution>> ComposeAsync(
        IReadOnlyList<ToolContribution> contributions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in contributions)
        {
            if (!seen.Add(c.Tool.Name))
                throw new InvalidOperationException(
                    $"Duplicate tool name '{c.Tool.Name}'. " +
                    "Built-in tools cannot be overridden silently — " +
                    "use an explicit namespace (e.g. mcp__server__tool) for additional providers.");
        }
        return ValueTask.FromResult<IReadOnlyList<ToolContribution>>(contributions);
    }
}
