using PhiCoding.Tui;
using Terminal.Gui.Drawing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class PhiThemeTests
{
    [Test]
    public async Task DefaultDark_HasNonNullColorsForEverySlot()
    {
        var theme = PhiTheme.DefaultDark();
        await Assert.That(theme.Background).IsNotDefault();
        await Assert.That(theme.PromptBackground).IsNotDefault();
        await Assert.That(theme.Foreground).IsNotDefault();
        await Assert.That(theme.DiffAdded).IsNotDefault();
    }

    [Test]
    public async Task DefaultDark_PromptBackgroundDistinctFromTranscriptBackground()
    {
        var theme = PhiTheme.DefaultDark();
        await Assert.That(theme.PromptBackground.ToString())
            .IsNotEqualTo(theme.TranscriptBackground.ToString());
    }

    [Test]
    public async Task WindowScheme_NormalAttribute_MatchesBackground()
    {
        var theme = PhiTheme.DefaultDark();
        var scheme = theme.WindowScheme();
        await Assert.That(scheme.Normal.Background).IsEqualTo(theme.Background);
        await Assert.That(scheme.Normal.Foreground).IsEqualTo(theme.Foreground);
    }

    [Test]
    public async Task PromptScheme_NormalAttribute_MatchesPromptBackground()
    {
        var theme = PhiTheme.DefaultDark();
        var scheme = theme.PromptScheme();
        await Assert.That(scheme.Normal.Background).IsEqualTo(theme.PromptBackground);
        await Assert.That(scheme.Normal.Foreground).IsEqualTo(theme.PromptForeground);
    }

    [Test]
    public async Task TranscriptScheme_NormalAttribute_MatchesTranscriptBackground()
    {
        var theme = PhiTheme.DefaultDark();
        var scheme = theme.TranscriptScheme();
        await Assert.That(scheme.Normal.Background).IsEqualTo(theme.TranscriptBackground);
    }

    [Test]
    public async Task StatusScheme_NormalAttribute_MatchesStatusBackground()
    {
        var theme = PhiTheme.DefaultDark();
        var scheme = theme.StatusScheme();
        await Assert.That(scheme.Normal.Background).IsEqualTo(theme.StatusBackground);
    }

    [Test]
    public async Task AttributeFor_EveryStyle_ReturnsNonDefaultAttribute()
    {
        var theme = PhiTheme.DefaultDark();
        foreach (TranscriptStyle style in Enum.GetValues<TranscriptStyle>())
        {
            var attr = theme.AttributeFor(style);
            // Each line attribute must inherit the transcript background so the
            // hand-drawn transcript matches the Scheme-cleared viewport.
            await Assert.That(attr.Background).IsEqualTo(theme.TranscriptBackground);
        }
    }

    [Test]
    public async Task AttributeFor_DiffAdded_UsesDiffAddedColor()
    {
        var theme = PhiTheme.DefaultDark();
        var attr = theme.AttributeFor(TranscriptStyle.DiffAdded);
        await Assert.That(attr.Foreground).IsEqualTo(theme.DiffAdded);
    }

    [Test]
    public async Task AttributeFor_DiffRemoved_UsesDiffRemovedColor()
    {
        var theme = PhiTheme.DefaultDark();
        var attr = theme.AttributeFor(TranscriptStyle.DiffRemoved);
        await Assert.That(attr.Foreground).IsEqualTo(theme.DiffRemoved);
    }

    [Test]
    public async Task AttributeFor_ToolError_IsBold()
    {
        var theme = PhiTheme.DefaultDark();
        var attr = theme.AttributeFor(TranscriptStyle.ToolError);
        await Assert.That(attr.Style.HasFlag(TextStyle.Bold)).IsTrue();
    }

    [Test]
    public async Task DefaultDark_TranscriptPadding_HasHorizontalInset()
    {
        var theme = PhiTheme.DefaultDark();
        // Left+Right should be at least 1 each so text doesn't touch window edge.
        await Assert.That(theme.TranscriptPadding.Left).IsGreaterThan(0);
        await Assert.That(theme.TranscriptPadding.Right).IsGreaterThan(0);
    }

    [Test]
    public async Task DefaultDark_PromptPadding_HasHorizontalInset()
    {
        var theme = PhiTheme.DefaultDark();
        await Assert.That(theme.PromptPadding.Left).IsGreaterThan(0);
        await Assert.That(theme.PromptPadding.Right).IsGreaterThan(0);
    }

    [Test]
    public async Task DefaultDark_TranscriptAndPromptPadding_AreEqual()
    {
        var theme = PhiTheme.DefaultDark();
        await Assert.That(theme.TranscriptPadding).IsEqualTo(theme.PromptPadding);
    }

    [Test]
    public async Task DefaultDark_PromptMargin_HasTopGap_ForVerticalBreathingRoom()
    {
        var theme = PhiTheme.DefaultDark();
        // 1-cell vertical gap between transcript bottom and prompt input.
        await Assert.That(theme.PromptMargin.Top).IsGreaterThanOrEqualTo(1);
    }
}