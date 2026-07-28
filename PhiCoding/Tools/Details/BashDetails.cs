namespace PhiCoding.Tools.Details;

public sealed record BashDetails(
    string Command,
    int ExitCode,
    long DurationMs,
    string Stdout,
    string Stderr);