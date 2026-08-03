using PhiCoding.Resources;

namespace PhiCoding.Tests.Resources;

public class ProjectContextLoaderTests : IDisposable
{
    private readonly string _root;

    public ProjectContextLoaderTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"phi-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Mkdir(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Test]
    public async Task Load_NoAgentsFiles_ReturnsEmpty()
    {
        var cwd = Mkdir("pkg", "src");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).IsEmpty();
        await Assert.That(resources.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_RootAgentsFile_OnlyIncludesRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var pkg = Mkdir("pkg");
        var cwd = Path.Combine(pkg, "src");
        Directory.CreateDirectory(cwd);
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "root rules");
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "cwd rules");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles.Select(f => f.AbsolutePath))
            .IsEquivalentTo([
                Path.Combine(_root, "AGENTS.md"),
                Path.Combine(cwd, "AGENTS.md"),
            ]);
    }

    [Test]
    public async Task Load_IncludesAllIntermediateAgentsFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var pkg = Mkdir("pkg");
        var sub = Path.Combine(pkg, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "root");
        File.WriteAllText(Path.Combine(pkg, "AGENTS.md"), "pkg");
        File.WriteAllText(Path.Combine(sub, "AGENTS.md"), "sub");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = sub });

        await Assert.That(resources.ContextFiles.Select(f => Path.GetFileName(f.AbsolutePath)))
            .IsEquivalentTo(["AGENTS.md", "AGENTS.md", "AGENTS.md"]);
        var ordered = resources.ContextFiles
            .Select(f => Path.GetDirectoryName(f.AbsolutePath)!)
            .Select(Path.GetFileName)
            .ToArray();
        var expected = new[] { _root, pkg, sub }.Select(Path.GetFileName).ToArray();
        await Assert.That(ordered).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Load_OrderingIsRootFirst_ThenDescendants()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var pkg = Mkdir("pkg");
        var sub = Path.Combine(pkg, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "root");
        File.WriteAllText(Path.Combine(pkg, "AGENTS.md"), "pkg");
        File.WriteAllText(Path.Combine(sub, "AGENTS.md"), "sub");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = sub });

        var contents = resources.ContextFiles.Select(f => f.Content).ToArray();
        await Assert.That(contents[0]).IsEqualTo("root");
        await Assert.That(contents[1]).IsEqualTo("pkg");
        await Assert.That(contents[2]).IsEqualTo("sub");
    }

    [Test]
    public async Task Load_NoProjectRoot_OnlyScansCwd()
    {
        var cwd = Mkdir("a", "b");
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "local");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).Count().IsEqualTo(1);
        await Assert.That(resources.ContextFiles[0].Content).IsEqualTo("local");
    }

    [Test]
    public async Task Load_IgnoresDotPhiAndDotAgentsDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var cwd = Mkdir("pkg");
        Directory.CreateDirectory(Path.Combine(cwd, ".phi"));
        Directory.CreateDirectory(Path.Combine(cwd, ".agents"));
        File.WriteAllText(Path.Combine(cwd, ".phi", "AGENTS.md"), "phi rules");
        File.WriteAllText(Path.Combine(cwd, ".agents", "AGENTS.md"), "agents rules");
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "cwd rules");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).Count().IsEqualTo(1);
        await Assert.That(resources.ContextFiles[0].Content).IsEqualTo("cwd rules");
    }

    [Test]
    public async Task Load_IgnoresHomeGlobalAgentsFiles()
    {
        // Simulate a user home with AGENTS.md that must NOT be picked up.
        // We can't safely write to ~ here, so we instead construct a layout
        // where the project root is below a directory whose parent contains
        // a stray AGENTS.md.
        var outer = Mkdir("outer-home");
        File.WriteAllText(Path.Combine(outer, "AGENTS.md"), "ignored");
        var inner = Path.Combine(outer, "project");
        Directory.CreateDirectory(inner);
        Directory.CreateDirectory(Path.Combine(inner, ".git"));
        var cwd = Path.Combine(inner, "src");
        Directory.CreateDirectory(cwd);

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).IsEmpty();
    }

    [Test]
    public async Task Load_OversizeFile_AddsDiagnosticAndSkips()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var cwd = Mkdir("pkg");
        var huge = new string('x', ProjectContextLoader.MaxFileSizeBytes + 1);
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), huge);

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).IsEmpty();
        await Assert.That(resources.Diagnostics).Count().IsEqualTo(1);
        var d = resources.Diagnostics[0];
        await Assert.That(d.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(d.Message).Contains("exceeding");
        await Assert.That(d.Message).Contains(ProjectContextLoader.MaxFileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task Load_ExactlyAtLimit_IsAccepted()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var cwd = Mkdir("pkg");
        var atLimit = new string('y', ProjectContextLoader.MaxFileSizeBytes);
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), atLimit);

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).Count().IsEqualTo(1);
        await Assert.That(resources.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Load_ContentIsReadAsPlainText_NoFrontmatterStrip()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var cwd = Mkdir("pkg");
        var body = "---\nname: my-agent\ndescription: hello\n---\n\nreal body";
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), body);

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = cwd });

        await Assert.That(resources.ContextFiles).Count().IsEqualTo(1);
        await Assert.That(resources.ContextFiles[0].Content).IsEqualTo(body);
    }
}
