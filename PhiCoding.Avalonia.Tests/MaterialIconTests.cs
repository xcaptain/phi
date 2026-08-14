using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// Material.Icons.Avalonia wiring: the app registers <c>MaterialIconStyles</c>
/// so <see cref="MaterialIcon"/> controls resolve their control template.
/// Without it, icons render as empty boxes — this test guards the app-style
/// registration (README requirement for Material.Icons.Avalonia 2.0+).
/// </summary>
[NotInParallel("Avalonia-UI")]
public class MaterialIconTests
{
    [Test]
    public async Task Icon_ResolvesTemplate_AfterRealization()
    {
        AvaloniaTestHost.EnsureInitialized();

        var icon = new MaterialIcon { Kind = MaterialIconKind.ArrowUpward };
        var window = new Window { Width = 200, Height = 200, Content = icon };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await Assert.That(icon.Template).IsNotNull();
        window.Close();
    }

    [Test]
    public async Task IconKind_Set_RendersDrawingGeometry()
    {
        AvaloniaTestHost.EnsureInitialized();

        var icon = new MaterialIcon { Kind = MaterialIconKind.Folder };

        await Assert.That(icon.Drawing).IsNotNull();
        await Assert.That(icon.Drawing.Geometry).IsNotNull();
        await Assert.That(icon.Drawing.Geometry.Bounds.Width).IsGreaterThan(0);
    }
}
