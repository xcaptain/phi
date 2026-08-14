using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PhiCoding.Avalonia;

/// <summary>
/// Semantic colors resolved from the active Avalonia theme variant. The
/// TUI used ANSI named colors (red/green/yellow/dim); these map to fixed
/// hex pairs chosen per light/dark variant so component code never
/// hardcodes a one-theme color. Brushes are cached per variant.
/// </summary>
public static class AvaloniaTheme
{
    /// <summary>Secondary / dimmed text (path, token counters, thinking).</summary>
    public static IBrush TextSecondary => Pick(0xFF6B7280, 0xFF9CA3AF);

    /// <summary>Error text (persistent failures).</summary>
    public static IBrush Danger => Pick(0xFFC42B1C, 0xFFFF8A8A);

    /// <summary>Error background tint.</summary>
    public static IBrush DangerBackground => Pick(0xFFFDECEA, 0xFF3B1F1F);

    /// <summary>Success text (completed tool calls).</summary>
    public static IBrush Success => Pick(0xFF1B7F3B, 0xFF6FCF97);

    /// <summary>Border color for bubbles / pickers.</summary>
    public static IBrush ControlBorder => Pick(0xFFD1D5DB, 0xFF4B5563);

    /// <summary>Bubble background.</summary>
    public static IBrush ContainerBackground => Pick(0xFFF3F4F6, 0xFF1F2937);

    /// <summary>Accent used for the submit button.</summary>
    public static IBrush Accent => Pick(0xFF7C3AED, 0xFFA78BFA);

    /// <summary>Text drawn on top of <see cref="Accent"/>.</summary>
    public static IBrush AccentText => new SolidColorBrush(Colors.White);

    private static readonly Dictionary<uint, IBrush> Cache = [];

    private static IBrush Pick(uint light, uint dark)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var argb = isDark ? dark : light;
        if (!Cache.TryGetValue(argb, out var brush))
            Cache[argb] = brush = new SolidColorBrush(Color.FromUInt32(argb));
        return brush;
    }
}
