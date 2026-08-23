namespace Phi.Extensions;

/// <summary>
/// Capabilities an extension declares it needs. Recorded in
/// <see cref="PhiExtensionAttribute.Capabilities"/>; in v1.5+ the host
/// enforces strict mode where undeclared capabilities cause the
/// corresponding <see cref="IPhiApi"/> call to throw
/// <see cref="ExtensionError"/>.
/// <para>
/// v1 is "transparent": the audit log flags mismatches but doesn't block.
/// v1.5 enables strict mode per-extension (or globally via
/// <c>--enforce-capabilities</c>).
/// </para>
/// </summary>
[Flags]
public enum ExtensionCapability
{
    None               = 0,

    /// <summary>Outbound network (HTTP, gRPC, etc.).</summary>
    Network            = 1 << 0,

    /// <summary>Read files anywhere on the host filesystem.</summary>
    FileSystemRead     = 1 << 1,

    /// <summary>Write / mutate files anywhere on the host filesystem.</summary>
    FileSystemWrite    = 1 << 2,

    /// <summary>Spawn child processes (e.g. <c>Process.Start</c>).</summary>
    ProcessSpawn       = 1 << 3,

    /// <summary>Read credentials store, OAuth tokens, env-var secrets.</summary>
    SecretsRead        = 1 << 4,

    /// <summary>Read host environment variables.</summary>
    EnvironmentRead    = 1 << 5,

    /// <summary>Read system clipboard.</summary>
    ClipboardRead      = 1 << 6,

    /// <summary>Write system clipboard.</summary>
    ClipboardWrite     = 1 << 7,

    /// <summary>Show dialogs / request user interaction via <see cref="IPhiUiBridge"/>.</summary>
    UiInteract         = 1 << 8,

    /// <summary>Write transcript lines / submit messages.</summary>
    TranscriptWrite    = 1 << 9,
}
