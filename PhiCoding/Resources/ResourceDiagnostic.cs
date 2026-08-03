namespace PhiCoding.Resources;

/// <summary>
/// One non-fatal issue encountered while loading session resources. The
/// prompt builder does not read diagnostics directly; they are surfaced
/// through <c>SessionState</c> for UI display.
/// </summary>
public sealed record ResourceDiagnostic(
    string Source,
    string Message,
    DiagnosticSeverity Severity);
