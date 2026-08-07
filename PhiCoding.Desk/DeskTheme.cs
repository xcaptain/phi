using Aprillz.MewUI;

namespace PhiCoding.Desk;

/// <summary>
/// Semantic colors resolved from the active MewUI theme palette. The
/// TUI used ANSI named colors (red/green/yellow/dim); MewUI's
/// <see cref="Palette"/> doesn't expose semantic named slots, so these
/// map to the closest palette tokens.
/// </summary>
public static class DeskTheme
{
    /// <summary>Secondary / dimmed text (path, token counters, thinking).</summary>
    public static Color TextSecondary(Theme theme) => theme.Palette.PlaceholderText;

    /// <summary>Error text (persistent failures).</summary>
    public static Color Danger(Theme theme) =>
        theme.IsDark ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0xC4, 0x2B, 0x1C);

    /// <summary>Error background tint.</summary>
    public static Color DangerBackground(Theme theme) =>
        theme.Palette.ContainerBackground;

    /// <summary>Success text (completed tool calls).</summary>
    public static Color Success(Theme theme) =>
        theme.IsDark ? Color.FromRgb(0x6F, 0xCF, 0x97) : Color.FromRgb(0x1B, 0x7F, 0x3B);
}