using Avalonia.Media;
using PhiCoding.Avalonia.Components;
using PhiCoding.Prompt;

namespace PhiCoding.Avalonia.Tests;

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
    public async Task ModelForeground_Normal_IsDefault()
    {
        // null foreground → theme default text.
        var result = PickerItemStyles.ModelForeground.Convert(Model(), null!, null!, null!);
        await Assert.That(result).IsNull();
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
    public async Task WorkspaceForeground_SentinelIsDimmed_ElseDefault()
    {
        await Assert.That(PickerItemStyles.WorkspaceForeground.Convert(Workspace(isSentinel: true), null!, null!, null!))
            .IsEqualTo(AvaloniaTheme.TextSecondary);
        await Assert.That(PickerItemStyles.WorkspaceForeground.Convert(Workspace(isSentinel: false), null!, null!, null!))
            .IsNull();
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