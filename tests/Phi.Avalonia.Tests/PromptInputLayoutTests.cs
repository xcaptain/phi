using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Phi.Avalonia.Components;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="PromptInputLayout"/>: pure-declarative prompt-input chrome —
/// a single rounded Border hosting the editor and a bottom toolbar with
/// the model picker, the workspace picker, and the submit button.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class PromptInputLayoutTests
{
    private static PromptInputLayout CreateLayout()
    {
        AvaloniaTestHost.EnsureInitialized();
        return new PromptInputLayout();
    }

    /// <summary>Walks through the 1:8:1 reading-column Grid to the input Border.</summary>
    private static Border ShellOf(PromptInputLayout layout)
    {
        var grid = (Grid)layout.Content!;
        return (Border)grid.Children[0];
    }

    [Test]
    public async Task Root_IsUserControl_WrappingReadingColumn()
    {
        var layout = CreateLayout();
        await Assert.That(layout).IsAssignableTo<UserControl>();

        // Content is now a 1:8:1 reading-column Grid matching the transcript.
        var grid = (Grid)layout.Content!;
        await Assert.That(grid.ColumnDefinitions.Count).IsEqualTo(3);

        var shell = ShellOf(layout);
        await Assert.That(shell.CornerRadius).IsEqualTo(new CornerRadius(12));
        await Assert.That(shell.BorderThickness).IsEqualTo(new Thickness(1));
        // The input sits in the center (8*) column so it aligns with the
        // conversation content; horizontal breathing comes from the grid.
        await Assert.That(Grid.GetColumn(shell)).IsEqualTo(1);
        await Assert.That(shell.Margin.Left).IsEqualTo(0);
        await Assert.That(shell.Margin.Right).IsEqualTo(0);
        await Assert.That(shell.Margin.Bottom).IsEqualTo(12);
    }

    [Test]
    public async Task Stack_HasEditorOnTop_AndFooterBelow()
    {
        var layout = CreateLayout();
        var shell = ShellOf(layout);
        var stack = (StackPanel)shell.Child!;

        await Assert.That(stack.Children.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(stack.Children[0], layout.Editor)).IsTrue();
        await Assert.That(ReferenceEquals(stack.Children[1], layout.Footer)).IsTrue();
    }

    [Test]
    public async Task Editor_IsBorderlessMultilineTextInput()
    {
        var layout = CreateLayout();
        await Assert.That(layout.Editor.AcceptsReturn).IsTrue();
        await Assert.That(layout.Editor.TextWrapping).IsEqualTo(TextWrapping.Wrap);
        await Assert.That(layout.Editor.MinHeight).IsEqualTo(48);
        // SukiUI's NoShadow class keeps it transparent / borderless (even on
        // focus); the class is the declarative contract, applied by theme.
        await Assert.That(layout.Editor.Classes.Contains("NoShadow")).IsTrue();
        await Assert.That(layout.Editor.PlaceholderText).IsEqualTo("Ask Phi anything…");
    }

    [Test]
    public async Task Footer_DocksPickersLeftAndSubmitRight()
    {
        var layout = CreateLayout();
        var footer = layout.Footer;

        await Assert.That(footer.LastChildFill).IsTrue();
        await Assert.That(footer.Children.Contains(layout.ModelCombo)).IsTrue();
        await Assert.That(footer.Children.Contains(layout.WorkspaceCombo)).IsTrue();
        await Assert.That(footer.Children.Contains(layout.SubmitButton)).IsTrue();

        await Assert.That(DockPanel.GetDock(layout.ModelCombo)).IsEqualTo(Dock.Left);
        await Assert.That(DockPanel.GetDock(layout.WorkspaceCombo)).IsEqualTo(Dock.Left);
        await Assert.That(DockPanel.GetDock(layout.SubmitButton)).IsEqualTo(Dock.Right);
    }

    [Test]
    public async Task Pickers_AreFlatToolbarComboBoxes()
    {
        var layout = CreateLayout();
        foreach (var combo in new[] { layout.ModelCombo, layout.WorkspaceCombo })
        {
            await Assert.That(combo.MinWidth).IsEqualTo(180);
            await Assert.That(combo.Background).IsEqualTo(Brushes.Transparent);
            await Assert.That(combo.BorderThickness).IsEqualTo(new Thickness(0));
        }
        await Assert.That(layout.ModelCombo.PlaceholderText).IsEqualTo("Select model");
        await Assert.That(layout.WorkspaceCombo.PlaceholderText).IsEqualTo("Select workspace");
    }

    [Test]
    public async Task SubmitButton_IsCircular_WithArrowIcon()
    {
        var layout = CreateLayout();
        await Assert.That(layout.SubmitButton.Width).IsEqualTo(34);
        await Assert.That(layout.SubmitButton.Height).IsEqualTo(34);
        await Assert.That(layout.SubmitButton.CornerRadius).IsEqualTo(new CornerRadius(17));

        var icon = layout.SubmitIcon;
        await Assert.That(icon.Width).IsEqualTo(18);
        await Assert.That(icon.Height).IsEqualTo(18);
        await Assert.That(icon.Kind).IsEqualTo(Material.Icons.MaterialIconKind.ArrowUpward);
    }

    [Test]
    public async Task Pickers_ResolveDataTemplatesFromStaticResource()
    {
        // The dropdown row templates are declared as XAML resources and
        // wired via ItemTemplate="{StaticResource ...}". If the templates
        // are ever removed, the pickers render their items as raw records
        // instead of styled rows — this pins that wiring.
        var layout = CreateLayout();
        await Assert.That(layout.ModelCombo.ItemTemplate).IsNotNull();
        await Assert.That(layout.WorkspaceCombo.ItemTemplate).IsNotNull();
    }
}
