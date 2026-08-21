namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Resolves the <see cref="IToolCard"/> implementation for a given tool name.
/// Adding a new tool means writing one <see cref="ToolCardBase"/> subclass and
/// adding a switch arm here.
/// </summary>
public static class ToolCardRegistry
{
    public static IToolCard For(string name) => name switch
    {
        "read" => new ReadToolCard(),
        "write" => new WriteToolCard(),
        "edit" => new EditToolCard(),
        "bash" => new BashToolCard(),
        _ => new GenericToolCard(),
    };
}
