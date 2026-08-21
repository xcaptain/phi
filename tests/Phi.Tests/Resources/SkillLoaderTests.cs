using Phi.Resources;

namespace Phi.Tests.Resources;

public class SkillLoaderTests : IDisposable
{
    private readonly string _home;
    private readonly string _projectRoot;
    private readonly string _cwd;

    public SkillLoaderTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"phi-skills-home-{Guid.NewGuid():N}");
        _projectRoot = Path.Combine(Path.GetTempPath(), $"phi-skills-proj-{Guid.NewGuid():N}");
        _cwd = Path.Combine(_projectRoot, "sub", "deep");
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static void WriteSkill(string root, string name, string body, string description)
    {
        var dir = Path.Combine(root, ".agents", "skills", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n---\n{body}\n");
    }

    private static SkillLoadOptions OptionsFor(string home, string cwd) => new()
    {
        Cwd = cwd,
        HomeDir = home,
    };

    [Test]
    public async Task Load_UserLevelSkill_Discovered()
    {
        WriteSkill(_home, "skill-a", "body a", "User description");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Name).IsEqualTo("skill-a");
        await Assert.That(result.Skills[0].Description).IsEqualTo("User description");
        await Assert.That(result.Skills[0].Source).IsEqualTo("user");
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_ProjectLevelSkill_Discovered()
    {
        // .git marker so ProjectRootLocator returns _projectRoot
        Directory.CreateDirectory(Path.Combine(_projectRoot, ".git"));
        WriteSkill(_projectRoot, "skill-b", "body b", "Project description");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Name).IsEqualTo("skill-b");
        await Assert.That(result.Skills[0].Source).IsEqualTo("project");
    }

    [Test]
    public async Task Load_NoProjectRoot_OnlyUserLevel()
    {
        // _projectRoot has no marker; ProjectRootLocator returns null
        WriteSkill(_home, "user-only", "u", "u");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Source).IsEqualTo("user");
    }

    [Test]
    public async Task Load_SameNameAcrossSources_KeepsFirstSkill_Warns()
    {
        Directory.CreateDirectory(Path.Combine(_projectRoot, ".git"));
        WriteSkill(_home, "shared", "user body", "User version");
        WriteSkill(_projectRoot, "shared", "project body", "Project version");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        // User skills load first, so per the Agent Skills standard the user
        // version is kept and the collision is reported as a warning.
        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Source).IsEqualTo("user");
        await Assert.That(result.Skills[0].Description).IsEqualTo("User version");
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(result.Diagnostics[0].Message).Contains("collides");
    }

    [Test]
    public async Task Load_DirectoryWithoutSKILLmd_IsSkipped()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "not-a-skill");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "README.md"), "not a skill");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).IsEmpty();
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_OversizeFile_SkippedWithDiagnostic()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "huge");
        Directory.CreateDirectory(dir);
        var huge = new string('x', SkillLoader.MaxFileSizeBytes + 1);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            $"---\nname: huge\ndescription: d\n---\n{huge}\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).IsEmpty();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(result.Diagnostics[0].Message).Contains("exceeding");
    }

    [Test]
    public async Task Load_ExactlyAtLimit_Accepted()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "exact");
        Directory.CreateDirectory(dir);
        var fmLen = "---\nname: exact\ndescription: d\n---\n\n".Length;
        var body = new string('y', SkillLoader.MaxFileSizeBytes - fmLen);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            $"---\nname: exact\ndescription: d\n---\n{body}\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_NoFrontmatter_SkipsSkill_MissingDescription()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "no-fm");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "just a body, no frontmatter\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        // No frontmatter means no description, which is the one fatal
        // validation case — the skill is not loaded.
        await Assert.That(result.Skills).IsEmpty();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("description is required");
    }

    [Test]
    public async Task Load_DuplicateAcrossSources_KeepsFirstUserSkill()
    {
        Directory.CreateDirectory(Path.Combine(_projectRoot, ".git"));
        WriteSkill(_projectRoot, "zebra", "p", "p");
        WriteSkill(_home, "alpha", "u", "u");
        WriteSkill(_projectRoot, "alpha", "p", "p");
        WriteSkill(_home, "zebra", "u", "u");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        // The user copies load first, so both duplicate project copies are
        // dropped with warnings; the kept skills order by source then name.
        await Assert.That(result.Skills.Select(s => s.Name)).IsEquivalentTo(["alpha", "zebra"]);
        await Assert.That(result.Skills.All(s => s.Source == "user")).IsTrue();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(2);
        await Assert.That(result.Diagnostics.All(d => d.Message.Contains("collides"))).IsTrue();
    }

    // ──────── Validation (Agent Skills standard) ────────

    [Test]
    public async Task Load_MissingDescription_SkipsSkill_WithWarning()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "no-desc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: no-desc\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).IsEmpty();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(result.Diagnostics[0].Message).Contains("description is required");
    }

    [Test]
    public async Task Load_EmptyDescription_SkipsSkill()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "empty-desc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: empty-desc\ndescription:\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).IsEmpty();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("description is required");
    }

    [Test]
    public async Task Load_NameTooLong_WarnsButStillLoads()
    {
        var longName = new string('n', 65);
        var dir = Path.Combine(_home, ".agents", "skills", longName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            $"---\nname: {longName}\ndescription: d\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Name).IsEqualTo(longName);
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(result.Diagnostics[0].Message).Contains("exceeds 64");
    }

    [Test]
    public async Task Load_NameInvalidCharacters_WarnsButStillLoads()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "PDF-Processing");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: PDF-Processing\ndescription: d\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Name).IsEqualTo("PDF-Processing");
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("invalid characters");
    }

    [Test]
    public async Task Load_NameHyphenViolations_WarnButStillLoad()
    {
        // Frontmatter names differ from the directory names so the hyphen
        // rules are exercised without needing odd directory names.
        var violations = new (string Dir, string FmName)[]
        {
            ("a", "-lead"),
            ("b", "trail-"),
            ("c", "doub--le"),
        };
        foreach (var (dirName, fmName) in violations)
        {
            var dir = Path.Combine(_home, ".agents", "skills", dirName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"),
                $"---\nname: {fmName}\ndescription: d\n---\nbody\n");
        }

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(3);
        await Assert.That(result.Diagnostics).Count().IsEqualTo(3);
        await Assert.That(result.Diagnostics.All(d => d.Message.Contains("hyphen"))).IsTrue();
    }

    [Test]
    public async Task Load_DescriptionTooLong_WarnsButStillLoads()
    {
        var longDesc = new string('d', 1025);
        var dir = Path.Combine(_home, ".agents", "skills", "long-desc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            $"---\nname: long-desc\ndescription: {longDesc}\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Description).IsEqualTo(longDesc);
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("exceeds 1024");
    }

    [Test]
    public async Task Load_FrontmatterNameDiffersFromDirectory_Loads_NoWarning()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "dir-name");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: declared-name\ndescription: d\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Name).IsEqualTo("declared-name");
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_UnknownFrontmatterFields_Ignored()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "extra");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: extra\ndescription: d\nlicense: MIT\nmetadata:\n  x: 1\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_InvalidFrontmatter_SkipsSkill_ProducesDiagnostic()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: broken\ndescription: d\nno closing fence\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).IsEmpty();
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Message).Contains("unterminated");
    }

    [Test]
    public async Task Load_IgnoresPhiDirectories_OnlyAgents()
    {
        // user explicitly wants to NOT search .phi paths
        var phiDir = Path.Combine(_home, ".phi", "skills", "phi-skill");
        Directory.CreateDirectory(phiDir);
        File.WriteAllText(Path.Combine(phiDir, "SKILL.md"),
            "---\nname: phi-skill\ndescription: d\n---\nbody\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).IsEmpty();
    }
}
