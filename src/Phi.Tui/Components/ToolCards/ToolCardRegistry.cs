using Phi.Chat;
using Phi.Extensions.Rendering;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Resolves the <see cref="IToolCard"/> implementation for a given tool name.
/// An extension can override the card for a custom tool via
/// <c>IPhiApi.RegisterToolCard</c>; when a renderer is registered for the
/// name (passed through <paramref name="renderers"/>), the card wraps it.
/// Otherwise a built-in switch produces the card for the known tools
/// (read / write / edit / bash) with a generic fallback.
/// </summary>
public static class ToolCardRegistry
{
    public static IToolCard For(string name, IExtensionRenderers? renderers = null)
    {
        // Extension-registered card renderer wins; the returned fragment
        // (a XenoAtom Visual) becomes the card body on completion.
        if (renderers?.TryGetToolCardRenderer(name, out var r) == true
            && r is ToolCardRenderer renderer)
        {
            return new CustomToolCard(renderer);
        }

        return name switch
        {
            "read" => new ReadToolCard(),
            "write" => new WriteToolCard(),
            "edit" => new EditToolCard(),
            "bash" => new BashToolCard(),
            _ => new GenericToolCard(),
        };
    }
}
