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
/// agent run starts. Handlers set <see cref="Result"/> to transform the
/// text or short-circuit the prompt.
/// <para>
/// The <c>Result</c> slot follows the C# event pattern (like
/// <c>CancelEventArgs</c>): the <c>IPhiApi.On("input", ...)</c> handler
/// sets it, and the hook dispatcher reads it after all handlers run.
/// Null result = pass-through.
/// </para>
/// </summary>
public sealed record InputEvent(
    string Text,
    InputSource Source,
    bool Streaming) : PhiEvent
{
    /// <summary>
    /// Set by a handler to transform (<see cref="InputHookResult.Text"/>) or
    /// consume (<see cref="InputHookResult.Handled"/>) the input. Null =
    /// pass-through.
    /// </summary>
    public InputHookResult? Result { get; set; }
}

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
