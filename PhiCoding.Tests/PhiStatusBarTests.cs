using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class PhiStatusBarTests
{
    [Test]
    public async Task FormatCount_SmallNumbers_Raw()
    {
        await Assert.That(PhiStatusBar.FormatCount(0)).IsEqualTo("0");
        await Assert.That(PhiStatusBar.FormatCount(999)).IsEqualTo("999");
    }

    [Test]
    public async Task FormatCount_Thousands_KSuffix()
    {
        await Assert.That(PhiStatusBar.FormatCount(1500)).IsEqualTo("1.5k");
        await Assert.That(PhiStatusBar.FormatCount(999_999)).IsEqualTo("1000.0k");
    }

    [Test]
    public async Task FormatCount_Millions_MSuffix()
    {
        await Assert.That(PhiStatusBar.FormatCount(2_500_000)).IsEqualTo("2.5M");
    }

    [Test]
    public async Task ShortenPath_UnderHome_UsesTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await Assert.That(PhiStatusBar.ShortenPath(home + "/github/phi")).IsEqualTo("~/github/phi");
    }

    [Test]
    public async Task ShortenPath_OutsideHome_Unchanged()
    {
        await Assert.That(PhiStatusBar.ShortenPath("/var/tmp/x")).IsEqualTo("/var/tmp/x");
    }
}
