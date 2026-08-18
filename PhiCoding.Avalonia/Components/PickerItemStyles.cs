using Avalonia.Data.Converters;
using Avalonia.Media;
using PhiCoding.Prompt;

namespace PhiCoding.Avalonia.Components;

/// <summary>
/// Value converters backing the prompt input pickers' XAML
/// <c>DataTemplate</c>s. The picker items are UI-agnostic records
/// (<see cref="ModelPickerItem"/> / <see cref="WorkspacePickerItem"/>),
/// so the "which style for which row" mapping lives here — a small,
/// testable Avalonia-side translation that the templates consume via
/// <c>{x:Static}</c>.
/// </summary>
public static class PickerItemStyles
{
    /// <summary>
    /// Model row foreground: provider header rows are dimmed, the
    /// currently-active model is accented, everything else uses the
    /// default foreground (null → theme default).
    /// </summary>
    public static FuncValueConverter<ModelPickerItem, IBrush?> ModelForeground { get; } = new(
        item => item is null ? null
              : item.IsHeader ? AvaloniaTheme.TextSecondary
              : item.IsCurrent ? AvaloniaTheme.Accent
              : null);

    /// <summary>Model row font weight: header bold, current semibold, else normal.</summary>
    public static FuncValueConverter<ModelPickerItem, FontWeight> ModelFontWeight { get; } = new(
        item => item is null ? FontWeight.Normal
              : item.IsHeader ? FontWeight.Bold
              : item.IsCurrent ? FontWeight.SemiBold
              : FontWeight.Normal);

    /// <summary>Workspace row foreground: the "Choose folder…" sentinel is dimmed.</summary>
    public static FuncValueConverter<WorkspacePickerItem, IBrush?> WorkspaceForeground { get; } = new(
        item => item is null || !item.IsSentinel ? null : AvaloniaTheme.TextSecondary);

    /// <summary>Workspace row font weight: sentinel semibold, else normal.</summary>
    public static FuncValueConverter<WorkspacePickerItem, FontWeight> WorkspaceFontWeight { get; } = new(
        item => item is not null && item.IsSentinel ? FontWeight.SemiBold : FontWeight.Normal);
}