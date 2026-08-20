using Phi.Slash;

namespace Phi.Tests;

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
        await Assert.That(candidates).IsEquivalentTo(SlashCommands.All);
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

    [Test]
    public async Task All_IncludesConnectAndModels()
    {
        await Assert.That(SlashCommands.All).Contains("/connect");
        await Assert.That(SlashCommands.All).Contains("/models");
    }

    [Test]
    public async Task MatchWithArgs_ArgCommandWithArg_Parses()
    {
        var parsed = SlashCommands.MatchWithArgs("/connect minimax");
        await Assert.That(parsed).IsNotNull();
        await Assert.That(parsed!.Value.Command).IsEqualTo("/connect");
        await Assert.That(parsed.Value.Args).IsEqualTo("minimax");
    }

    [Test]
    public async Task MatchWithArgs_ModelsNoLongerTakesArgs_ReturnsNull()
    {
        // /models is dialog-only now; "/models glm-5.1" is a plain prompt.
        await Assert.That(SlashCommands.MatchWithArgs("/models glm-5.1")).IsNull();
    }

    [Test]
    public async Task MatchWithArgs_ExactCommandNoArgs_ReturnsNull()
    {
        await Assert.That(SlashCommands.MatchWithArgs("/connect")).IsNull();
        await Assert.That(SlashCommands.MatchWithArgs("/models")).IsNull();
    }

    [Test]
    public async Task MatchWithArgs_NonArgCommand_ReturnsNull()
    {
        // /exit keeps its strict exact-match contract: "/exit now" is a prompt.
        await Assert.That(SlashCommands.MatchWithArgs("/exit now")).IsNull();
        await Assert.That(SlashCommands.MatchWithArgs("/sessions")).IsNull();
    }

    [Test]
    public async Task MatchWithArgs_UnknownCommand_ReturnsNull()
    {
        await Assert.That(SlashCommands.MatchWithArgs("/bogus arg")).IsNull();
        await Assert.That(SlashCommands.MatchWithArgs("hello world")).IsNull();
    }

    [Test]
    public async Task MatchSkill_ColonSyntax_ExtractsName()
    {
        var parsed = SlashCommands.MatchSkill("/skill:dotnet-testing");
        await Assert.That(parsed).IsNotNull();
        await Assert.That(parsed!.Value.SkillName).IsEqualTo("dotnet-testing");
        await Assert.That(parsed.Value.Prompt).IsNull();
    }

    [Test]
    public async Task MatchSkill_WithTrailingPrompt_SplitsNameAndPrompt()
    {
        var parsed = SlashCommands.MatchSkill("/skill:find-skills 找一个写小说的技能");
        await Assert.That(parsed).IsNotNull();
        await Assert.That(parsed!.Value.SkillName).IsEqualTo("find-skills");
        await Assert.That(parsed.Value.Prompt).IsEqualTo("找一个写小说的技能");
    }

    [Test]
    public async Task MatchSkill_TrailingWhitespaceOnly_NoPrompt()
    {
        var parsed = SlashCommands.MatchSkill("/skill:dotnet-testing   ");
        await Assert.That(parsed).IsNotNull();
        await Assert.That(parsed!.Value.SkillName).IsEqualTo("dotnet-testing");
        await Assert.That(parsed.Value.Prompt).IsNull();
    }

    [Test]
    public async Task MatchSkill_NoPrefix_ReturnsNull()
    {
        await Assert.That(SlashCommands.MatchSkill("dotnet-testing")).IsNull();
        await Assert.That(SlashCommands.MatchSkill("/sessions")).IsNull();
        await Assert.That(SlashCommands.MatchSkill("/skill")).IsNull();
    }

    [Test]
    public async Task MatchSkill_EmptyName_ReturnsNull()
    {
        await Assert.That(SlashCommands.MatchSkill("/skill:")).IsNull();
        await Assert.That(SlashCommands.MatchSkill("/skill:   ")).IsNull();
    }
}
