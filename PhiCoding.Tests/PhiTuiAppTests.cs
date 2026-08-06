using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="PhiTuiApp"/>: the route shell. It resolves the current route to
/// a page and mounts it in the page host; page behaviors are tested on the
/// pages themselves. This only smoke-tests that the shell builds a host.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class PhiTuiAppTests
{
    [Test]
    public async Task BuildRoot_ReturnsHost_ForCurrentRoute()
    {
        var session = new MockSession();
        var app = new PhiTuiApp(new FakeSessionNavigator(session), new ProviderManager());

        var root = app.BuildRoot();

        await Assert.That(root).IsNotNull();
        await Assert.That(root).IsTypeOf<ComputedVisual>();
    }
}
