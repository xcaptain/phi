using PhiCoding.Avalonia.Controls;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="EllipsisMenu"/>: the compact "⋮" button opens a menu with the
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
        var icon = (global::Material.Icons.Avalonia.MaterialIcon)menu.Content!;
        await Assert.That(icon.Kind).IsEqualTo(global::Material.Icons.MaterialIconKind.DotsHorizontal);
    }
}
