namespace Phi.Extensions.Host;

/// <summary>
/// Maps <see cref="IPhiApi"/> action methods to the
/// <see cref="ExtensionCapability"/> flags they exercise. The runtime
/// consults this table on every action invocation: declared-but-unused
/// capabilities are fine (over-declaration), undeclared-but-used
/// capabilities are either audit-logged (v1 transparent) or block the
/// call with <see cref="ExtensionError"/> (v1.5 strict).
/// <para>
/// Setup-time methods (<c>RegisterTool</c>, <c>RegisterCommand</c>,
/// <c>AddPromptGuideline</c>, <c>On</c>) deliberately don't appear here
/// — those are registration, not resource access. Resource access flows
/// through the tools / commands the extension registers, which carry
/// their own <see cref="ToolCapabilities"/>.
/// </para>
/// </summary>
internal static class CapabilityActionMap
{
    /// <summary>
    /// Returns the <see cref="ExtensionCapability"/> flags the given
    /// <see cref="IPhiApi"/> action method requires, or <c>null</c> if
    /// the method is not governed by capabilities (registration, identity,
    /// events). Used by <see cref="PhiApi"/> to drive
    /// <see cref="ExtensionRuntime.EnsureCapability"/>.
    /// </summary>
    public static ExtensionCapability? RequiredFor(string methodName) => methodName switch
    {
        // ─── UiInteract: anything that talks to the user ───
        nameof(IPhiApi.Notify) => ExtensionCapability.UiInteract,

        // ─── TranscriptWrite: anything that injects chat content ───
        nameof(IPhiApi.SubmitUserMessage) => ExtensionCapability.TranscriptWrite,
        nameof(IPhiApi.SubmitCustomMessage) => ExtensionCapability.TranscriptWrite,
        nameof(IPhiApi.SubmitTranscriptLine) => ExtensionCapability.TranscriptWrite,

        // SwitchModel / SwitchProvider / AppendEntryAsync are
        // session-internal state mutations — they don't touch host
        // resources (network / fs / process / ui) and so don't
        // require capability declarations. They are also rarer than
        // the four above; if a future Sprint adds a host-touching
        // action, wire it here.
        _ => null,
    };
}
