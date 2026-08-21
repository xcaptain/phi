using Phi.Agent;
using Phi.Tools;

namespace Phi;

public static class BuiltInTools
{
    /// <summary>
    /// Creates the four built-in tools bound to <paramref name="cwd"/> so
    /// relative paths and <c>bash</c> resolve against the session root.
    /// </summary>
    public static IReadOnlyList<Tool> CreateDefault(string cwd) =>
    [
        new BashTool(cwd),
        new ReadTool(cwd),
        new WriteTool(cwd),
        new EditTool(cwd),
    ];

    /// <summary>
    /// Creates the four built-in tools bound to the process working
    /// directory. Kept for tests and ad-hoc callers that have not been
    /// migrated to a session-cwd model yet.
    /// </summary>
    public static IReadOnlyList<Tool> CreateDefault() =>
        CreateDefault(Environment.CurrentDirectory);
}
