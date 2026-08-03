using PhiCoding.Resources;

namespace PhiCoding.Tests.Resources;

public class SkillFrontmatterParserTests
{
    [Test]
    public async Task Parse_CompleteFrontmatter_ExtractsNameAndDescription()
    {
        var content = "---\nname: dotnet-testing\ndescription: Write xUnit tests\n---\n# body\n";

        var result = SkillFrontmatterParser.Parse("directory-name", "/abs/skills/dotnet-testing/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("dotnet-testing");
        await Assert.That(result.Description).IsEqualTo("Write xUnit tests");
        await Assert.That(result.Body).IsEqualTo("# body\n");
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Parse_NoFrontmatter_FallsBackToDirectoryName_AndEmptyDescription()
    {
        var content = "# Just a body\nWith two lines.\n";

        var result = SkillFrontmatterParser.Parse("my-skill", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("my-skill");
        await Assert.That(result.Description).IsNull();
        await Assert.That(result.Body).IsEqualTo(content);
    }

    [Test]
    public async Task Parse_MissingDescription_ProducesDiagnostic_NullDescription()
    {
        var content = "---\nname: foo\n---\nbody\n";

        var result = SkillFrontmatterParser.Parse("foo", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("foo");
        await Assert.That(result.Description).IsNull();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("description");
    }

    [Test]
    public async Task Parse_MissingName_UsesDirectoryName_AndDiagnostic()
    {
        var content = "---\ndescription: only description\n---\nbody\n";

        var result = SkillFrontmatterParser.Parse("dir-name", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("dir-name");
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("'name'");
    }

    [Test]
    public async Task Parse_UnterminatedFrontmatter_ReturnsNullName_AndDiagnostic()
    {
        var content = "---\nname: foo\ndescription: bar\nno closing fence\nbody\n";

        var result = SkillFrontmatterParser.Parse("foo", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsNull();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("unterminated");
    }

    [Test]
    public async Task Parse_QuotedValue_TrimsQuotes()
    {
        var content = "---\nname: \"quoted-name\"\ndescription: 'a value'\n---\nbody\n";

        var result = SkillFrontmatterParser.Parse("dir", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("quoted-name");
        await Assert.That(result.Description).IsEqualTo("a value");
    }

    [Test]
    public async Task Parse_ExtraKeys_AreIgnored()
    {
        var content = "---\nname: foo\ndescription: bar\nother: ignored\n# comment\n---\nbody\n";

        var result = SkillFrontmatterParser.Parse("foo", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("foo");
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Parse_WindowsLineEndings_Handled()
    {
        var content = "---\r\nname: foo\r\ndescription: bar\r\n---\r\nbody\r\n";

        var result = SkillFrontmatterParser.Parse("foo", "/abs/SKILL.md", content);

        await Assert.That(result.Name).IsEqualTo("foo");
        await Assert.That(result.Body).IsEqualTo("body\n");
    }
}