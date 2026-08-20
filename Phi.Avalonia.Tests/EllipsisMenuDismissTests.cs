using Avalonia.Controls;
using Avalonia.Threading;
using Phi.Avalonia.Controls;
using Phi.Avalonia.Tests.Helpers;

namespace Phi.Avalonia.Tests;

[NotInParallel("Avalonia-UI")]
public class EllipsisMenuDismissTests
{
    [Test]
    public async Task OutsidePointerPress_DismissesTheMenu()
    {
        // Light-dismiss: clicking anywhere outside the menu closes it. Open
        // the menu, then press on a sibling element outside the popup.
        AvaloniaTestHost.EnsureInitialized();
        var menu = new EllipsisMenu();
        menu.AddItem("Rename", () => { });
        var outside = new Button { Content = "elsewhere" };
        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = new StackPanel
            {
                Children = { menu, outside },
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        PointerInputSimulator.LeftClick(menu.Trigger);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(menu.Menu.IsOpen).IsTrue();

        // Press outside the menu — the popup's light-dismiss must close it.
        PointerInputSimulator.LeftClick(outside);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(menu.Menu.IsOpen).IsFalse();
        window.Close();
    }
}