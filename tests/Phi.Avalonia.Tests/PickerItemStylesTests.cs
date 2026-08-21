using Avalonia.Media;
using Phi.Avalonia.Components;
using Phi.Prompt;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="PickerItemStyles"/>: the converters that style picker rows
/// in XAML. Mapping table:
/// <list type="bullet">
/// <item>Model header row → dim + bold</item>
/// <item>Model current row → accent + semibold</item>
/// <item>Workspace sentinel row → dim + semibold</item>
/// <item>everything else → theme default (null foreground / normal)</item>
/// </list>
/// </summary>
[NotInParallel("Avalonia-UI")]
public class PickerItemStylesTests
{
    private static ModelPickerItem Model(bool isHeader = false, bool isCurrent = false) => new()
    {
        Label = "row",
        IsHeader = isHeader,
        IsCurrent = isCurrent,
    };

    private static WorkspacePickerItem Workspace(bool isSentinel) => new()
    {
        Label = "row",
        IsSentinel = isSentinel,
        Cwd = isSentinel ? string.Empty : "/cwd",
    };

    [Test]
    public async Task ModelForeground_Header_IsDimmed()
    {
        var result = PickerItemStyles.ModelForeground.Convert(Model(isHeader: true), null!, null!, null!);
        await Assert.That(result).IsEqualTo(AvaloniaTheme.TextSecondary);
    }

    [Test]
    public async Task ModelForeground_Current_IsAccent()
    {
        var result = PickerItemStyles.ModelForeground.Convert(Model(isCurrent: true), null!, null!, null!);
        await Assert.That(result).IsEqualTo(AvaloniaTheme.Accent);
    }

    [Test]
    public async Task ModelForeground_Normal_IsPrimaryText()
    {
        // A normal (non-header, non-current) row must be readable — an
        // explicit primary-text brush, NOT null (a null brush renders the
        // row invisible rather than inheriting).
        var result = PickerItemStyles.ModelForeground.Convert(Model(), null!, null!, null!);
        await Assert.That(result).IsEqualTo(AvaloniaTheme.TextPrimary);
    }

    [Test]
    public async Task ModelFontWeight_HeaderIsBold_CurrentSemiBold_NormalIsNormal()
    {
        await Assert.That(PickerItemStyles.ModelFontWeight.Convert(Model(isHeader: true), null!, null!, null!))
            .IsEqualTo(FontWeight.Bold);
        await Assert.That(PickerItemStyles.ModelFontWeight.Convert(Model(isCurrent: true), null!, null!, null!))
            .IsEqualTo(FontWeight.SemiBold);
        await Assert.That(PickerItemStyles.ModelFontWeight.Convert(Model(), null!, null!, null!))
            .IsEqualTo(FontWeight.Normal);
    }

    [Test]
    public async Task WorkspaceForeground_SentinelIsDimmed_ElsePrimaryText()
    {
        await Assert.That(PickerItemStyles.WorkspaceForeground.Convert(Workspace(isSentinel: true), null!, null!, null!))
            .IsEqualTo(AvaloniaTheme.TextSecondary);
        // Non-sentinel rows use the primary text brush (never null).
        await Assert.That(PickerItemStyles.WorkspaceForeground.Convert(Workspace(isSentinel: false), null!, null!, null!))
            .IsEqualTo(AvaloniaTheme.TextPrimary);
    }

    [Test]
    public async Task WorkspaceFontWeight_SentinelIsSemiBold_ElseNormal()
    {
        await Assert.That(PickerItemStyles.WorkspaceFontWeight.Convert(Workspace(isSentinel: true), null!, null!, null!))
            .IsEqualTo(FontWeight.SemiBold);
        await Assert.That(PickerItemStyles.WorkspaceFontWeight.Convert(Workspace(isSentinel: false), null!, null!, null!))
            .IsEqualTo(FontWeight.Normal);
    }
}
