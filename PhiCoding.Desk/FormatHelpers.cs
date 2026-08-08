namespace PhiCoding.Desk;

/// <summary>
/// Small formatting helpers shared by the Desk UI components. Replaces the
/// removed <c>StatusBarView.FormatCount</c>.
/// </summary>
internal static class FormatHelpers
{
    public static string FormatSeconds(int seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60.0:F1}m",
        _ => $"{seconds / 3600.0:F1}h",
    };
}