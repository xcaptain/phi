namespace Phi.Extensions.Rendering;

/// <summary>
/// Converts a <see cref="TranscriptLine"/> DTO into the host's
/// chat-line representation (a <c>Phi.Chat.ChatLine</c> subclass on
/// either UI). Returned as <see cref="object"/> to keep this contract
/// UI-framework-free; the host casts.
/// <para>
/// v1 return type is intentionally permissive — once the host's
/// <c>ChatTranscriptProjector</c> learns to dispatch by line type, this
/// can be tightened to <c>Phi.Chat.ChatLine</c> without breaking callers
/// (cast in renderer body is the only impact).
/// </para>
/// </summary>
public delegate object TranscriptLineRenderer(
    TranscriptLine Line,
    bool Expanded);
