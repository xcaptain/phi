namespace Phi.Extensions;

/// <summary>
/// DTO that an extension submits to <see cref="IPhiApi.SubmitTranscriptLine"/>.
/// The host renders it via the renderer registered for <see cref="Type"/>
/// (see <see cref="IPhiApi.RegisterTranscriptLineRenderer"/>); if no renderer
/// is registered, the host falls back to a plain <c>TextContentBlock</c>.
/// <para>
/// Kept UI-framework-free so extensions don't depend on Avalonia / XenoAtom
/// types. The renderer is the only place that crosses the boundary into
/// <c>Phi.Chat.ChatLine</c> (TUI / Avalonia specific).
/// </para>
/// </summary>
/// <param name="Type">
/// Discriminator the host uses to look up a renderer. Convention:
/// <c>"&lt;extension-name&gt;:&lt;kind&gt;"</c>, e.g. <c>"multi-agent:subagent-progress"</c>.
/// </param>
/// <param name="Id">Stable line id (used by the projector for DIFF rendering).</param>
/// <param name="Content">Plain-text body shown when the line is collapsed.</param>
/// <param name="Details">
/// Optional structured payload the renderer can read (e.g. role, status,
/// percentage for a progress bar). Free-form <c>object?</c> values — the
/// renderer is responsible for safe casting.
/// </param>
public sealed record TranscriptLine(
    string Type,
    string Id,
    string Content,
    IReadOnlyDictionary<string, object?>? Details = null);
