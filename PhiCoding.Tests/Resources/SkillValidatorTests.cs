using PhiCoding.Resources;

namespace PhiCoding.Tests.Resources;

public class SkillValidatorTests
{
    [Test]
    public async Task ValidateName_ValidName_NoErrors()
    {
        await Assert.That(SkillValidator.ValidateName("dotnet-testing")).IsEmpty();
    }

    [Test]
    public async Task ValidateName_Exceeds64Chars_Warns()
    {
        var errors = SkillValidator.ValidateName(new string('n', 65));

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0]).Contains("exceeds 64");
    }

    [Test]
    public async Task ValidateName_InvalidCharacters_Warns()
    {
        var errors = SkillValidator.ValidateName("PDF-Processing");

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0]).Contains("invalid characters");
    }

    [Test]
    public async Task ValidateName_LeadingOrTrailingHyphen_Warns()
    {
        var leading = SkillValidator.ValidateName("-lead");
        var trailing = SkillValidator.ValidateName("trail-");

        await Assert.That(leading).Count().IsEqualTo(1);
        await Assert.That(leading[0]).Contains("start or end with a hyphen");
        await Assert.That(trailing).Count().IsEqualTo(1);
        await Assert.That(trailing[0]).Contains("start or end with a hyphen");
    }

    [Test]
    public async Task ValidateName_ConsecutiveHyphens_Warns()
    {
        var errors = SkillValidator.ValidateName("foo--bar");

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0]).Contains("consecutive hyphens");
    }

    [Test]
    public async Task ValidateName_MultipleViolations_AllReported()
    {
        // Invalid chars + edge hyphens + consecutive hyphens = 3 distinct
        // violations (edge hyphens are one combined rule).
        var errors = SkillValidator.ValidateName("-Foo--");

        await Assert.That(errors).Count().IsEqualTo(3);
    }

    [Test]
    public async Task ValidateDescription_Missing_IsRequired()
    {
        await Assert.That(SkillValidator.ValidateDescription(null)).IsEquivalentTo(["description is required"]);
        await Assert.That(SkillValidator.ValidateDescription("")).IsEquivalentTo(["description is required"]);
        await Assert.That(SkillValidator.ValidateDescription("   ")).IsEquivalentTo(["description is required"]);
    }

    [Test]
    public async Task ValidateDescription_Exceeds1024Chars_Warns()
    {
        var errors = SkillValidator.ValidateDescription(new string('d', 1025));

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0]).Contains("exceeds 1024");
    }

    [Test]
    public async Task ValidateDescription_Exactly1024Chars_NoErrors()
    {
        await Assert.That(SkillValidator.ValidateDescription(new string('d', 1024))).IsEmpty();
    }

    [Test]
    public async Task ValidateDescription_Valid_NoErrors()
    {
        await Assert.That(SkillValidator.ValidateDescription("Write xUnit tests.")).IsEmpty();
    }
}
