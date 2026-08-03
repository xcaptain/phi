using PhiCoding.Tui;

namespace PhiCoding.Tests;

public class SlashCommandCatalogTests
{
    [Test]
    public async Task All_NamesMatchExecutableCommands()
    {
        await Assert.That(SlashCommandCatalog.All.Select(c => c.Name))
            .IsEquivalentTo(SlashCommands.All);
    }

    [Test]
    public async Task All_NamesAreUnique()
    {
        await Assert.That(SlashCommandCatalog.All.Select(c => c.Name).Distinct().Count())
            .IsEqualTo(SlashCommandCatalog.All.Count);
    }

    [Test]
    public async Task All_HaveDescriptions()
    {
        foreach (var command in SlashCommandCatalog.All)
        {
            await Assert.That(command.Description).IsNotEmpty();
        }
    }

    [Test]
    public async Task All_HaveNamesStartingWithSlash()
    {
        foreach (var command in SlashCommandCatalog.All)
        {
            await Assert.That(command.Name).StartsWith("/");
        }
    }

    [Test]
    public async Task All_ArgTakingCommands_HaveUsage()
    {
        foreach (var command in SlashCommandCatalog.All.Where(c => c.SupportsArgs))
        {
            await Assert.That(command.Usage).IsNotEmpty();
        }
    }

    [Test]
    public async Task Find_ByName_CaseInsensitive()
    {
        await Assert.That(SlashCommandCatalog.Find("CONNECT")!.Name).IsEqualTo("/connect");
    }

    [Test]
    public async Task Find_Unknown_ReturnsNull()
    {
        await Assert.That(SlashCommandCatalog.Find("/nope")).IsNull();
    }
}
