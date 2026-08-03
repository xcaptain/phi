namespace PhiCoding.Resources;

/// <summary>
/// Severity of a <see cref="ResourceDiagnostic"/>. Phase 3 only emits
/// <see cref="Warning"/> for recoverable issues (oversize file, IO
/// failure); the loader never escalates to <see cref="Error"/> because a
/// bad AGENTS.md must not prevent a session from starting.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}
