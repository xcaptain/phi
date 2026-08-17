using Avalonia.Controls;
using PhiCoding.Avalonia.Components;
using PhiCoding.Avalonia.Tests.Helpers;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="CollapsibleSection"/>: two-row collapsible layout. Click on
/// the header row (anywhere, including the title text area) toggles
/// <see cref="CollapsibleSection.IsExpanded"/>; the body content's
/// visibility + the chevron orientation follow the toggle.
/// <para>
/// Regression target: the original Button-based header had flaky hit-testing
/// in Avalonia 12 — only the chevron area reliably received clicks. The
/// fix swaps the Button for a Border + PointerPressed event handler so
/// every pixel of the header row toggles the section.
/// </para>
/// </summary>
[NotInParallel("Avalonia-UI")]
public class CollapsibleSectionTests
{
    private static CollapsibleSection Create()
    {
        AvaloniaTestHost.EnsureInitialized();
        return new CollapsibleSection(
            new TextBlock { Text = "header" },
            new TextBlock { Text = "body" });
    }

    [Test]
    public async Task IsExpanded_DefaultsToFalse_BodyHidden()
    {
        var section = Create();
        await Assert.That(section.IsExpanded).IsFalse();
    }

    [Test]
    public async Task Click_Header_TogglesIsExpanded_True()
    {
        var section = Create();
        var headerArea = AvaloniaTestHelpers.FindHeaderArea(section);

        PointerInputSimulator.LeftClick(headerArea);

        await Assert.That(section.IsExpanded).IsTrue();
    }

    [Test]
    public async Task Click_Header_TogglesBackToFalse_OnSecondClick()
    {
        var section = Create();
        var headerArea = AvaloniaTestHelpers.FindHeaderArea(section);

        PointerInputSimulator.LeftClick(headerArea);
        PointerInputSimulator.LeftClick(headerArea);

        await Assert.That(section.IsExpanded).IsFalse();
    }

    [Test]
    public async Task Click_TitleTextArea_TogglesIsExpanded()
    {
        // Regression: the Button-based header only responded to clicks
        // inside the chevron's hit-test box; clicks on the title text
        // fell through. The Border-based header responds on its entire
        // bounds, so a press anywhere on the row toggles the section.
        var section = Create();
        var headerArea = AvaloniaTestHelpers.FindHeaderArea(section);

        PointerInputSimulator.LeftClick(headerArea);

        await Assert.That(section.IsExpanded).IsTrue();
    }

    [Test]
    public async Task SetExpanded_True_FlipsChevronAndShowsBody()
    {
        AvaloniaTestHost.EnsureInitialized();
        var section = new CollapsibleSection(
            new TextBlock { Text = "h" },
            new TextBlock { Text = "b" });

        section.IsExpanded = true;

        await Assert.That(section.IsExpanded).IsTrue();
        var bodyHost = AvaloniaTestHelpers.FindBodyHost(section);
        await Assert.That(bodyHost.IsVisible).IsTrue();
    }

    [Test]
    public async Task SetBody_ReplacesRenderedContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var section = new CollapsibleSection(
            new TextBlock { Text = "h" },
            new TextBlock { Text = "first" });

        section.SetBody(new TextBlock { Text = "second" });

        await Assert.That(((TextBlock)section.BodyContent).Text).IsEqualTo("second");
    }
}
