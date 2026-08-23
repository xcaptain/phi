namespace Phi.Extensions.Rendering;

/// <summary>
/// Renders an assistant message that has a custom <c>ContentType</c>
/// (i.e. <see cref="IPhiApi.SubmitCustomMessage"/> was used). Same
/// permissive return-type as <see cref="TranscriptLineRenderer"/>: the
/// renderer returns whatever the host knows how to render; if the host
/// doesn't know, the message falls back to a plain text rendering.
/// </summary>
public delegate object MessageRenderer(
    string ContentType,
    string Content,
    IReadOnlyDictionary<string, object?>? Details);
