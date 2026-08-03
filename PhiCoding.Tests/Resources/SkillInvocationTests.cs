using PhiCoding.Resources;

namespace PhiCoding.Tests.Resources;

public class SkillInvocationTests
{
    [Test]
    public async Task Build_ProducesPiStyleSkillBlock_AndRoundTripsThroughParse()
    {
        var content = SkillInvocation.Build(
            "dotnet-testing",
            "/abs/dotnet-testing/SKILL.md",
            "/abs/dotnet-testing",
            "Write xUnit tests.\nUse references/xunit.md.");

        await Assert.That(content).IsEqualTo(
            "<skill name=\"dotnet-testing\" location=\"/abs/dotnet-testing/SKILL.md\">\n" +
            "References are relative to /abs/dotnet-testing.\n\n" +
            "Write xUnit tests.\nUse references/xunit.md.\n</skill>");

        await Assert.That(SkillInvocation.TryParse(content, out var block)).IsTrue();
        await Assert.That(block!.Name).IsEqualTo("dotnet-testing");
        await Assert.That(block.Location).IsEqualTo("/abs/dotnet-testing/SKILL.md");
        await Assert.That(block.Content).IsEqualTo(
            "References are relative to /abs/dotnet-testing.\n\n" +
            "Write xUnit tests.\nUse references/xunit.md.");
        await Assert.That(block.UserMessage).IsNull();
    }

    [Test]
    public async Task Build_WithArgs_AppendsUserMessage_AfterTheBlock()
    {
        var content = SkillInvocation.Build(
            "dotnet-testing", "/abs/SKILL.md", "/abs", "body", args: "translate to spanish");

        await Assert.That(SkillInvocation.TryParse(content, out var block)).IsTrue();
        await Assert.That(block!.Content).IsEqualTo("References are relative to /abs.\n\nbody");
        await Assert.That(block.UserMessage).IsEqualTo("translate to spanish");
    }

    [Test]
    public async Task TryParse_PlainText_ReturnsFalse() =>
        await Assert.That(SkillInvocation.TryParse("just a prompt", out _)).IsFalse();

    [Test]
    public async Task TryParse_PartialSkillTag_ReturnsFalse() =>
        await Assert.That(SkillInvocation.TryParse("<skill name=\"x\">", out _)).IsFalse();

    [Test]
    public async Task TryParse_MissingClosingTag_ReturnsFalse() =>
        await Assert.That(SkillInvocation.TryParse(
            "<skill name=\"x\" location=\"y\">\nbody", out _)).IsFalse();

    [Test]
    public async Task TryParse_WhitespaceOnlyUserMessage_TreatedAsNull()
    {
        var content = SkillInvocation.Build("s", "/abs/SKILL.md", "/abs", "body") + "\n\n   ";

        await Assert.That(SkillInvocation.TryParse(content, out var block)).IsTrue();
        await Assert.That(block!.UserMessage).IsNull();
    }

    [Test]
    public async Task TryParse_LocationWithWindowsPath_Parses()
    {
        var content = SkillInvocation.Build(
            "pdf-tools", "C:\\skills\\pdf-tools\\SKILL.md", "C:\\skills\\pdf-tools", "body");

        await Assert.That(SkillInvocation.TryParse(content, out var block)).IsTrue();
        await Assert.That(block!.Location).IsEqualTo("C:\\skills\\pdf-tools\\SKILL.md");
    }
}
