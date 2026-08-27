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
    public async Task GetSuggestion_MidSentence_ReturnsNull()
    {
        // The strict line-start rule: a slash buried mid-sentence is just
        // text. Typing "please /con" must not surface the /connect command
        // — that's the bug the rule fixes.
        var text = "please /con";
        var match = Create().GetSuggestion(text, text.Length);

        await Assert.That(match).IsNull();
    }

    [Test]
    public async Task GetSuggestion_IndentedLine_ReturnsNull()
    {
        // Leading whitespace disqualifies the line — only a literal '/'
        // at position 0 counts.
        await Assert.That(Create().GetSuggestion("  /connect", 10)).IsNull();
        await Assert.That(Create().GetSuggestion("\t/exit", 5)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_NewlineBeforeCaret_DoesNotTrigger()
    {
        // The strict first-line rule: slash commands are single-line.
        // Crossing onto a continuation line disables the trigger even
        // though the buffer still starts with '/'.
        await Assert.That(Create().GetSuggestion("hello\n/con", 9)).IsNull();
        await Assert.That(Create().GetSuggestion("/exit\nfoo", 9)).IsNull();
        await Assert.That(Create().GetSuggestion("/exit\n", 6)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_AfterCommandArgs_Triggers()
    {
        // Once the first character is '/' and the caret hasn't crossed a
        // newline, typing arguments after the command token must keep
        // suggestions flowing — the trigger is "buffer starts with '/'",
        // not "caret on command token".
        var text = "/connect openai";
        var match = Create().GetSuggestion(text, text.Length);

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(["/connect"]);
        await Assert.That(match.ReplaceStart).IsEqualTo(0);
        await Assert.That(match.ReplaceLength).IsEqualTo("/connect".Length);
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
    public async Task GetSuggestion_SlashNotAtLineStart_ReturnsNull()
    {
        // "foo/bar" — first character is 'f', not '/'. The whole string is
        // the caret's line, so no command triggers.
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
