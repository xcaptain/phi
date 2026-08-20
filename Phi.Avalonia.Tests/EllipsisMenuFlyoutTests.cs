using Avalonia.Controls;
using Avalonia.Threading;
using Phi.Avalonia.Controls;
using Phi.Avalonia.Tests.Helpers;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="EllipsisMenu"/>: the ⋮ trigger is a Border whose full bounds
/// toggle the menu on pointer press (Button hit-testing is flaky in
/// Avalonia 12 — see CollapsibleSection). The standalone MenuFlyout is
/// light-dismissed, so clicking elsewhere closes it. These tests drive the
/// real pointer pipeline via <see cref="PointerInputSimulator"/>.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class EllipsisMenuFlyoutTests
{
    private static EllipsisMenu CreateShownMenu()
    {
        AvaloniaTestHost.EnsureInitialized();
        var menu = new EllipsisMenu();
        menu.AddItem("Rename", () => { });
        var window = new Window { Content = menu };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return menu;
    }

    [Test]
    public async Task PointerPress_OpensTheMenu()
    {
        var menu = CreateShownMenu();

        PointerInputSimulator.LeftClick(menu.Trigger);
        Dispatcher.UIThread.RunJobs();

        await Assert.That(menu.Menu.IsOpen).IsTrue();
        (menu.Parent as Window)?.Close();
    }

    [Test]
    public async Task PointerPress_AgainWhileOpen_ClosesIt()
    {
        var menu = CreateShownMenu();

        PointerInputSimulator.LeftClick(menu.Trigger);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(menu.Menu.IsOpen).IsTrue();

        // The trigger toggles: a second press closes it.
        PointerInputSimulator.LeftClick(menu.Trigger);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(menu.Menu.IsOpen).IsFalse();
        (menu.Parent as Window)?.Close();
    }

    [Test]
    public async Task Menu_IsLightDismissEnabled()
    {
        // Regression: clicking elsewhere must close the menu. PopupFlyoutBase
        // creates its Popup with IsLightDismissEnabled=true.
        var menu = new EllipsisMenu();
        menu.AddItem("Delete", () => { });
        await Assert.That(menu.Menu.Popup.IsLightDismissEnabled).IsTrue();
    }

    [Test]
    public async Task Menu_CanBeHidden()
    {
        var menu = CreateShownMenu();
        PointerInputSimulator.LeftClick(menu.Trigger);
        Dispatcher.UIThread.RunJobs();
        await Assert.That(menu.Menu.IsOpen).IsTrue();

        // The light-dismiss / selected-item closing path.
        menu.Menu.Hide();
        Dispatcher.UIThread.RunJobs();
        await Assert.That(menu.Menu.IsOpen).IsFalse();
        (menu.Parent as Window)?.Close();
    }
}