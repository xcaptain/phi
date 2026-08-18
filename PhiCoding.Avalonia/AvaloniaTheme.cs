using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PhiCoding.Avalonia;

/// <summary>
/// Semantic colors resolved from the active Avalonia theme variant. The
/// TUI used ANSI named colors (red/green/yellow/dim); these map to fixed
/// hex pairs chosen per light/dark variant so component code never
/// hardcodes a one-theme color. Brushes are cached per variant.
/// <para>
/// The hex pairs mirror SukiUI's palette (see
/// <c>SukiUI/ColorTheme/Light.axaml</c> + <c>Dark.axaml</c> and the
/// active <c>SukiColorTheme</c>), so the hand-drawn chrome (bubbles, tool
/// cards, sidebar) visually matches the SukiUI-themed controls. Light/dark
/// follows <see cref="Application.ActualThemeVariant"/> — SukiTheme's
/// <c>SwitchBaseTheme()</c> flips it, and the per-variant brush cache picks
/// the matching hex pair.
/// </para>
/// </summary>
public static class AvaloniaTheme
{
    /// <summary>Default body text (assistant messages, command output, etc.) → SukiUI <c>SukiText</c>.</summary>
    public static IBrush TextPrimary => Pick(0xFF222222, 0xFFEDFFFF);

    /// <summary>Secondary / dimmed text (path, token counters, thinking) → SukiUI <c>SukiLowText</c>.</summary>
    public static IBrush TextSecondary => Pick(0xFF555555, 0xFFCAFFFF);

    /// <summary>Error text (persistent failures) → SukiUI <c>SukiDangerColor</c>.</summary>
    public static IBrush Danger => Pick(0xFFF5222D, 0xFFF5222D);

    /// <summary>Error background tint (SukiUI has no danger-background semantic).</summary>
    public static IBrush DangerBackground => Pick(0xFFFDECEA, 0xFF3B1F1F);

    /// <summary>Success text (completed tool calls) → SukiUI <c>SukiSuccessColor</c>.</summary>
    public static IBrush Success => Pick(0xFF279F27, 0xFF228B22);

    /// <summary>Border color for bubbles / pickers → SukiUI <c>SukiControlBorderBrush</c>.</summary>
    public static IBrush ControlBorder => Pick(0xFFCECECE, 0xFF606060);

    /// <summary>Bubble background → SukiUI <c>SukiCardBackground</c>.</summary>
    public static IBrush ContainerBackground => Pick(0xFFFFFFFF, 0xFF323232);

    /// <summary>Accent used for the submit button → SukiUI <c>SukiPrimaryColor</c> (Blue theme).</summary>
    public static IBrush Accent => Pick(0xFF0A59F7, 0xFF0A59F7);

    /// <summary>Text drawn on top of <see cref="Accent"/> → SukiUI <c>HighlightForegroundColor</c>.</summary>
    public static IBrush AccentText { get; } = new SolidColorBrush(Colors.White);

    /// <summary>
    /// Monospace font family with a cross-platform fallback chain. Avalonia
    /// splits the string on <c>,</c> and walks the names in order, picking
    /// the first one that resolves to a usable <c>GlyphTypeface</c>; an
    /// unusable family throws at the first lookup, so the order matters —
    /// each platform's preferred font should be near the front.
    /// <para>
    /// Coverage by platform (font presence as observed on stock installs):
    /// <list type="bullet">
    /// <item>macOS / iOS / Mac Catalyst: <c>Menlo</c>, <c>Monaco</c></item>
    /// <item>Windows 10/11: <c>Consolas</c>, <c>Courier New</c></item>
    /// <item>Android 6+: <c>Noto Sans Mono</c>; older: <c>Droid Sans Mono</c></item>
    /// <item>Most Linux distros: <c>Liberation Mono</c>, <c>DejaVu Sans Mono</c></item>
    /// <item>Browser / WASM: <c>ui-monospace</c>, <c>monospace</c> (CSS generic)</item>
    /// </list>
    /// Adding a font later (e.g. <c>Roboto Mono</c>) only requires editing
    /// this constant — every code path that wants monospace already routes
    /// through <see cref="MonoFontFamily"/>.
    /// </para>
    /// </summary>
    public static FontFamily MonoFontFamily { get; } = new(
        // macOS / iOS first (the historical primary dev target).
        "Menlo, Monaco, " +
        // Windows.
        "Consolas, " +
        // Universal fallback shipped on every desktop OS.
        "Courier New, " +
        // Android (newer first, so older Android still resolves).
        "Noto Sans Mono, Droid Sans Mono, " +
        // Common Linux distros.
        "Liberation Mono, DejaVu Sans Mono, " +
        // Third-party monospace fonts devs often install.
        "Source Code Pro, Inconsolata, " +
        // CSS generics — last resort, mostly useful in the browser.
        "ui-monospace, monospace");

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
