using PhiCoding.Resources;

namespace PhiCoding.Tests.Resources;

public class ProjectRootLocatorTests : IDisposable
{
    private readonly string _root;

    public ProjectRootLocatorTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"phi-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task Locate_NoMarker_ReturnsNull()
    {
        var nested = Path.Combine(_root, "a", "b");
        Directory.CreateDirectory(nested);

        var result = ProjectRootLocator.Locate(nested);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Locate_GitMarker_ReturnsMarkerDir()
    {
        var nested = Path.Combine(_root, "pkg", "src");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(_root, ".git"));

        var result = ProjectRootLocator.Locate(nested);

        await Assert.That(result).IsEqualTo(_root);
    }

    [Test]
    public async Task Locate_SlnMarker_ReturnsMarkerDir()
    {
        var nested = Path.Combine(_root, "sub");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(_root, "app.sln"), "");

        var result = ProjectRootLocator.Locate(nested);

        await Assert.That(result).IsEqualTo(_root);
    }

    [Test]
    public async Task Locate_GitBeatsCsproj_InMonorepo()
    {
        var nested = Path.Combine(_root, "packages", "web");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "packages", "web", "web.csproj"), "");

        var result = ProjectRootLocator.Locate(nested);

        await Assert.That(result).IsEqualTo(_root);
    }

    [Test]
    public async Task Locate_CloserMarkerWins_WhenWalkingUp()
    {
        var pkg = Path.Combine(_root, "pkg");
        var nested = Path.Combine(pkg, "src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(pkg, "pkg.csproj"), "");

        var result = ProjectRootLocator.Locate(nested);

        await Assert.That(result).IsEqualTo(pkg);
    }

    [Test]
    public async Task Locate_StopsAtMarker_NeverWalksAbove()
    {
        var nested = Path.Combine(_root, "sub");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(_root, ".git"));

        var result = ProjectRootLocator.Locate(nested);

        await Assert.That(result).IsEqualTo(_root);
        await Assert.That(result).IsNotEqualTo(Path.GetDirectoryName(_root));
    }
}
