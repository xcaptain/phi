namespace Phi.Extensions.Events;

// ──────── User input hook ────────

/// <summary>How a streaming input event is being delivered.</summary>
public enum InputSource
{
    /// <summary>The TUI's <c>Editor.Accepted</c> event (user pressed Enter).</summary>
    EditorAccepted,

    /// <summary>An extension called <see cref="IPhiApi.SubmitUserMessage"/>.</summary>
    ExtensionSubmit,
}

/// <summary>
/// Fired when the user (or an extension) submits a prompt, before the
/// agent run starts. Handlers can transform the text (<c>return new
/// InputHookResult(transform: "...")</c>) or short-circuit
/// (<c>return new InputHookResult(handled: true)</c>) to consume the
/// prompt without running an agent turn.
/// </summary>
public sealed record InputEvent(
    string Text,
    InputSource Source,
    bool Streaming) : PhiEvent;

/// <summary>
/// Result of an input-hook handler. <c>Handled = true</c> short-circuits
/// the input (no agent run, no transcript write); <c>Text</c> replaces the
/// original input (after transformation by all handlers in chain order);
/// <c>Message</c> is an optional diagnostic written to the audit log
/// (e.g. "rewrote by my-ext:slash-command-routing").
/// </summary>
public sealed record InputHookResult
{
    public bool Handled { get; init; }
    public string? Text { get; init; }
    public string? Message { get; init; }

    /// <summary>Pass-through (no transformation, no short-circuit).</summary>
    public static readonly InputHookResult PassThrough = new() { Handled = false };
}
