using PhiCoding.Desk.Components;
using PhiCoding.Desk.Tests.Helpers;
using PhiCoding.Providers;

namespace PhiCoding.Desk.Tests.Components;

/// <summary>
/// <see cref="ModelsPage"/>: the model settings page lists the active
/// provider's models and switches the live session when one is selected.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class ModelsPageTests
{
    [Test]
    public async Task Build_NoProviderConnected_ShowsHint()
    {
        var session = new MockSession();
        var page = new ModelsPage(session, new ProviderManager());

        await Assert.That(page.Root).IsNotNull();
        await Assert.That(page.ModelsList).IsNull();
    }

    [Test]
    public async Task Build_WithProvider_ListsItsModels()
    {
        var session = new MockSession();
        session.UpdateState(s => s with { ProviderName = "deepseek", Model = "deepseek-v4-flash" });
        var page = new ModelsPage(session, new ProviderManager());

        await Assert.That(page.ModelsList).IsNotNull();
        // deepseek catalog: flash + pro
        await Assert.That(page.ModelsList!.ItemsSource.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SelectModel_CallsSessionSwitchModel()
    {
        var session = new MockSession();
        session.UpdateState(s => s with { ProviderName = "deepseek", Model = "deepseek-v4-flash" });
        var page = new ModelsPage(session, new ProviderManager());

        page.ModelsList!.SelectedIndex = 1;

        await Assert.That(session.LastSwitchedModel).IsEqualTo("deepseek-v4-pro");
    }
}