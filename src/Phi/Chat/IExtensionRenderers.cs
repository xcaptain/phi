using Phi.Agent;

namespace Phi.Chat;

/// <summary>
/// Bridge between <see cref="ChatTranscriptProjector"/> and the host's
/// extension runtime. Lets the projector (and the UI chat components)
/// consult extension-registered descriptors / card renderers / transcript
/// line renderers without taking a hard reference to
/// <c>Phi.Extensions.Host.ExtensionRuntime</c> — which would create a
/// cyclic dependency (<c>Phi</c> → <c>Phi.Extensions.Host</c> → <c>Phi</c>).
/// <para>
/// Renderer entries are typed as <see cref="object"/> — the implementation
/// returns whatever its concrete type is (a <c>XenoAtom.Visual</c> for
/// the TUI, an <c>Avalonia.Controls.Control</c> for Avalonia), and each
/// UI casts to the expected interface at the boundary. This keeps the
/// chat projection UI-framework-free while still letting extensions
/// produce rich visuals.
/// </para>
/// </summary>
public interface IExtensionRenderers
{
    /// <summary>
    /// Looks up a tool descriptor (icon + title + kind) for
    /// <paramref name="toolName"/>. Extensions override this to give a
    /// custom tool a custom icon/title; the host falls back to its
    /// built-in <c>ToolDescriptors.For</c> table when this returns
    /// <c>false</c>.
    /// </summary>
    bool TryGetToolDescriptor(
        string toolName,
        [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out ToolDescriptor descriptor);

    /// <summary>
    /// Looks up a tool card renderer for <paramref name="toolName"/>.
    /// The renderer produces the body content (markdown string or a
    /// host-specific visual); the host wraps it in its standard card
    /// layout. Returns <c>false</c> when no extension registered; the
    /// host falls back to its built-in card class.
    /// </summary>
    bool TryGetToolCardRenderer(string toolName, out object renderer);

    /// <summary>
    /// Looks up a transcript-line renderer for
    /// <paramref name="lineType"/>. Extensions register renderers per
    /// <c>Phi.Extensions.TranscriptLine.Type</c> string; the host invokes
    /// the renderer with the projected line and uses the returned
    /// host-specific fragment as the visual body. Returns <c>false</c>
    /// when no extension registered; the host falls back to plain-text
    /// rendering of the line's <c>Content</c>.
    /// </summary>
    bool TryGetTranscriptLineRenderer(string lineType, out object renderer);

    /// <summary>
    /// Looks up a message renderer for <paramref name="customType"/>.
    /// Extensions register renderers per <c>CustomMessage.CustomType</c>
    /// via <c>IPhiApi.RegisterMessageRenderer</c>; the host invokes the
    /// renderer with the custom message and uses the returned
    /// host-specific fragment as the visual body. Returns <c>false</c>
    /// when no extension registered; the host falls back to plain-text
    /// rendering of the message.
    /// </summary>
    bool TryGetMessageRenderer(string customType, out object renderer);
}
