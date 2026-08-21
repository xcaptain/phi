using Phi.Prompt;
using Phi.Slash;

namespace Phi.Tests;

public class SlashCommandProviderTests
{
    private static SlashCommandProvider Create() => new();

    [Test]
    public async Task GetSuggestion_BareSlash_ReturnsAllCommands()
    {
        var match = Create().GetSuggestion("/", 1);

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(SlashCommandCatalog.All.Select(c => c.Name));
        await Assert.That(match.ReplaceStart).IsEqualTo(0);
        await Assert.That(match.ReplaceLength).IsEqualTo(1);
    }

    [Test]
    public async Task GetSuggestion_Prefix_FiltersCaseInsensitively()
    {
        var match = Create().GetSuggestion("/MOD", 4);

        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(["/models"]);
    }

    [Test]
    public async Task GetSuggestion_MidSentence_TokenOnly()
    {
        var text = "please /con";
        var match = Create().GetSuggestion(text, text.Length);

        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(["/connect"]);
        // Replace span covers only the "/con" token, not the leading text.
        await Assert.That(match.ReplaceStart).IsEqualTo(7);
        await Assert.That(match.ReplaceLength).IsEqualTo(4);
    }

    [Test]
    public async Task GetSuggestion_NonSlashInput_ReturnsNull()
    {
        await Assert.That(Create().GetSuggestion("hello world", 11)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_EmptyInput_ReturnsNull()
    {
        await Assert.That(Create().GetSuggestion("", 0)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_SlashNotAtTokenStart_ReturnsNull()
    {
        // "/" is a token on its own; "foo/bar" has no command token.
        await Assert.That(Create().GetSuggestion("foo/bar", 7)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_UnknownPrefix_ReturnsNull()
    {
        await Assert.That(Create().GetSuggestion("/zzz", 4)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_DescriptionsAndLabelsMatchCatalog()
    {
        var match = Create().GetSuggestion("/connect", 8);
        var item = match!.Items.Single();

        await Assert.That(item.Label).IsEqualTo("/connect");
        await Assert.That(item.Description)
            .IsEqualTo(SlashCommandCatalog.Find("connect")!.Description);
        await Assert.That(item.Replacement).IsEqualTo("/connect");
    }
}
