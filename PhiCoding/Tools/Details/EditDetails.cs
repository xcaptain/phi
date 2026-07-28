namespace PhiCoding.Tools.Details;

public sealed record EditDetails(
    string Path,
    string OldString,
    string NewString,
    string Patch);