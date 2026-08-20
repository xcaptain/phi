using Phi.Prompts;
using Phi.Prompt;

namespace Phi.Tests.Tui;

public class SkillSuggestionProviderTests
{
    private static readonly IReadOnlyList<SkillDescriptor> Skills =
    [
        new() { Name = "dotnet-testing", Description = "Write xUnit tests", AbsolutePath = "/a/dotnet-testing/SKILL.md" },
        new() { Name = "docker", Description = "Containerize the app", AbsolutePath = "/a/docker/SKILL.md" },
        new() { Name = "drizzle", Description = "DB migrations", AbsolutePath = "/a/drizzle/SKILL.md" },
    ];

    private static SkillSuggestionProvider Provider() => new(Skills);

    [Test]
    public async Task GetSuggestion_BareSkillPrefix_ListsAllSkills()
    {
        var provider = Provider();

        var match = provider.GetSuggestion("/skill", 6);

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(["/skill:dotnet-testing", "/skill:docker", "/skill:drizzle"]);
    }

    [Test]
    public async Task GetSuggestion_ColonPrefix_FiltersByName()
    {
        var provider = Provider();

        var match = provider.GetSuggestion("/skill:dot", 10);

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(["/skill:dotnet-testing"]);
    }

    [Test]
    public async Task GetSuggestion_IncludesDescription()
    {
        var provider = Provider();

        var match = provider.GetSuggestion("/skill:docker", 12);

        await Assert.That(match!.Items[0].Description).IsEqualTo("Containerize the app");
    }

    [Test]
    public async Task GetSuggestion_ReplacementSpan_CoversOnlyTheToken()
    {
        var provider = Provider();

        // "ask /skill:dot then more" — caret inside the skill token
        var text = "ask /skill:dot then more";
        var caret = text.IndexOf("/skill:dot", StringComparison.Ordinal) + "/skill:dot".Length;

        var match = provider.GetSuggestion(text, caret);

        await Assert.That(match).IsNotNull();
        // replace start points at the '/', replace length is the token length
        await Assert.That(text[match!.ReplaceStart..].StartsWith("/skill:dot", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetSuggestion_CaseInsensitivePrefix()
    {
        var provider = Provider();

        var match = provider.GetSuggestion("/SKILL:DOT", 10);

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement))
            .IsEquivalentTo(["/skill:dotnet-testing"]);
    }

    [Test]
    public async Task GetSuggestion_NoSkillPrefix_ReturnsNull()
    {
        var provider = Provider();

        await Assert.That(provider.GetSuggestion("/connect", 8)).IsNull();
        await Assert.That(provider.GetSuggestion("hello", 5)).IsNull();
        await Assert.That(provider.GetSuggestion("/sess", 5)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_EmptyMatch_ReturnsNull()
    {
        var provider = Provider();

        await Assert.That(provider.GetSuggestion("/skill:zzz", 10)).IsNull();
    }

    [Test]
    public async Task GetSuggestion_EmptySkills_ReturnsNull()
    {
        var provider = new SkillSuggestionProvider([]);

        await Assert.That(provider.GetSuggestion("/skill", 6)).IsNull();
    }
}
