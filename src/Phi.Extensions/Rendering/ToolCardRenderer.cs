namespace Phi.Extensions.Rendering;

/// <summary>
/// Renders a tool card's body. The host (TUI / Avalonia) wraps the result
/// in the framework-appropriate layout — this delegate only emits the
/// content. Returns a plain string (e.g. markdown for both UIs to render
/// natively) or an arbitrary host-specific value when the extension
/// registered a custom renderer via
/// <see cref="IPhiApi.RegisterToolCard"/>.
/// </summary>
public delegate object? ToolCardRenderer(
    System.Text.Json.Nodes.JsonNode Arguments,
    Phi.Agent.ToolResult Result);
