using PhiCoding.Resources;

namespace PhiCoding.Tests.Resources;

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
    public async Task Load_ProjectOverridesUser_ForSameName()
    {
        Directory.CreateDirectory(Path.Combine(_projectRoot, ".git"));
        WriteSkill(_home, "shared", "user body", "User version");
        WriteSkill(_projectRoot, "shared", "project body", "Project version");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Source).IsEqualTo("project");
        await Assert.That(result.Skills[0].Description).IsEqualTo("Project version");
        await Assert.That(result.Diagnostics).Count().IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(result.Diagnostics[0].Message).Contains("overrides");
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
    public async Task Load_NoFrontmatter_UsesDirectoryName_AndDiagnostic()
    {
        var dir = Path.Combine(_home, ".agents", "skills", "no-fm");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "just a body, no frontmatter\n");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills).Count().IsEqualTo(1);
        await Assert.That(result.Skills[0].Name).IsEqualTo("no-fm");
        await Assert.That(result.Skills[0].Description).IsEmpty();
    }

    [Test]
    public async Task Load_OrderingIsProjectThenUser_ThenByName()
    {
        Directory.CreateDirectory(Path.Combine(_projectRoot, ".git"));
        WriteSkill(_projectRoot, "zebra", "p", "p");
        WriteSkill(_home, "alpha", "u", "u");
        WriteSkill(_projectRoot, "alpha", "p", "p");
        WriteSkill(_home, "zebra", "u", "u");

        var result = SkillLoader.Load(OptionsFor(_home, _cwd));

        await Assert.That(result.Skills.Select(s => s.Name)).IsEquivalentTo(["alpha", "zebra"]);
        await Assert.That(result.Skills[0].Source).IsEqualTo("project");
        await Assert.That(result.Skills[1].Source).IsEqualTo("project");
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
