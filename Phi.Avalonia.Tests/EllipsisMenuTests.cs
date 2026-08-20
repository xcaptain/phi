using Phi.Avalonia.Controls;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="EllipsisMenu"/>: the compact "⋮" trigger opens a menu with the
/// caller-supplied items.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class EllipsisMenuTests
{
    [Test]
    public async Task AddItem_AppendsItems()
    {
        AvaloniaTestHost.EnsureInitialized();

        var menu = new EllipsisMenu();
        await Assert.That(menu.ItemCount).IsEqualTo(0);

        menu.AddItem("Rename", () => { });
        menu.AddItem("Delete", () => { });

        await Assert.That(menu.ItemCount).IsEqualTo(2);
        await Assert.That(menu.Icon.Kind).IsEqualTo(global::Material.Icons.MaterialIconKind.DotsHorizontal);
    }

    [Test]
    public async Task Trigger_HasGenerousHitArea()
    {
        // Regression: the trigger must be easy to hit — at least 28×24 so
        // clicks land without precision, plus a full-bounds hit test.
        AvaloniaTestHost.EnsureInitialized();
        var menu = new EllipsisMenu();
        await Assert.That(menu.Trigger.MinWidth).IsGreaterThanOrEqualTo(28);
        await Assert.That(menu.Trigger.MinHeight).IsGreaterThanOrEqualTo(24);
        // The trigger is a Border (full-bounds hit test), not a Button whose
        // hit-testing is flaky in Avalonia 12.
        await Assert.That(menu.Trigger).IsTypeOf<global::Avalonia.Controls.Border>();
    }
}
