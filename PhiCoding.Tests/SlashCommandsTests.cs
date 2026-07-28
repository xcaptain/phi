using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class SlashCommandsTests
{
    [Test]
    public async Task Match_ExactCommand_ReturnsCanonical()
    {
        await Assert.That(SlashCommands.Match("/exit")).IsEqualTo("/exit");
    }

    [Test]
    public async Task Match_CaseInsensitive()
    {
        await Assert.That(SlashCommands.Match("/EXIT")).IsEqualTo("/exit");
    }

    [Test]
    public async Task Match_NormalText_ReturnsNull()
    {
        await Assert.That(SlashCommands.Match("hello")).IsNull();
        await Assert.That(SlashCommands.Match("/exit now")).IsNull();
        await Assert.That(SlashCommands.Match("/unknown")).IsNull();
        await Assert.That(SlashCommands.Match("/exi")).IsNull();
    }

    [Test]
    public async Task Complete_BareSlash_ReturnsAll()
    {
        var candidates = SlashCommands.Complete("/");
        await Assert.That(candidates).IsEquivalentTo(new[] { "/exit" });
    }

    [Test]
    public async Task Complete_Prefix_FiltersCandidates()
    {
        var candidates = SlashCommands.Complete("/ex");
        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0]).IsEqualTo("/exit");
    }

    [Test]
    public async Task Complete_NonSlashInput_ReturnsEmpty()
    {
        await Assert.That(SlashCommands.Complete("").Count).IsEqualTo(0);
        await Assert.That(SlashCommands.Complete("exit").Count).IsEqualTo(0);
    }
}
