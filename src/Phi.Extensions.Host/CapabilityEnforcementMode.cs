namespace Phi.Extensions.Host;

/// <summary>
/// How <see cref="ExtensionRuntime"/> reacts when an
/// <see cref="IPhiApi"/> action method requires a
/// <see cref="ExtensionCapability"/> the extension didn't declare on its
/// <see cref="PhiExtensionAttribute"/>.
/// <list type="bullet">
/// <item><see cref="Transparent"/> — log the mismatch to
/// <c>~/.phi/audit.log</c>, allow the call to proceed. This is the v1
/// default; it surfaces excess capabilities during development without
/// breaking existing extensions.</item>
/// <item><see cref="Strict"/> — throw <see cref="ExtensionError"/> and log
/// to <c>audit.log</c>. v1.5+: enables a security posture where every
/// capability-touching action must be pre-declared.</item>
/// </list>
/// </summary>
internal enum CapabilityEnforcementMode
{
    Transparent,
    Strict,
}
