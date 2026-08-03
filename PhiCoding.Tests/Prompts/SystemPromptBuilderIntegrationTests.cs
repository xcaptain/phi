using PhiCoding.Prompts;
using PhiCoding.Resources;

namespace PhiCoding.Tests.Prompts;

public class SystemPromptBuilderIntegrationTests : IDisposable
{
    private readonly string _root;

    public SystemPromptBuilderIntegrationTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"phi-prompt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task EndToEnd_AgentsFileFlowsIntoPrompt()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var pkg = Path.Combine(_root, "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "ROOT-RULES");
        File.WriteAllText(Path.Combine(pkg, "AGENTS.md"), "PKG-RULES");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = pkg });
        var prompt = new SystemPromptBuilder().Build(new SystemPromptBuildContext
        {
            Cwd = pkg,
            CurrentDate = new DateOnly(2026, 1, 1),
            Tools = [],
            Skills = [],
            ContextFiles = resources.ContextFiles,
            Options = new SystemPromptOptions(),
        });

        await Assert.That(prompt).Contains("ROOT-RULES");
        await Assert.That(prompt).Contains("PKG-RULES");
        var rootIdx = prompt.IndexOf("ROOT-RULES", StringComparison.Ordinal);
        var pkgIdx = prompt.IndexOf("PKG-RULES", StringComparison.Ordinal);
        await Assert.That(rootIdx).IsLessThan(pkgIdx);
    }
}
