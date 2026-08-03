using PhiCoding.Tools;

namespace PhiCoding.Tests.Tools;

public class WorkspacePathResolverTests
{
    [Test]
    public async Task RelativePath_ResolvesAgainstCwd()
    {
        var resolver = new WorkspacePathResolver("/work/phi");
        var resolved = resolver.Resolve("foo/bar.txt");
        await Assert.That(resolved).IsEqualTo("/work/phi/foo/bar.txt");
    }

    [Test]
    public async Task AbsolutePath_ReturnedUnchanged()
    {
        var resolver = new WorkspacePathResolver("/work/phi");
        var resolved = resolver.Resolve("/etc/hosts");
        await Assert.That(resolved).IsEqualTo("/etc/hosts");
    }

    [Test]
    public async Task DotSegment_IsNormalized()
    {
        var resolver = new WorkspacePathResolver("/work/phi");
        var resolved = resolver.Resolve("./a/../b/c.txt");
        await Assert.That(resolved).IsEqualTo("/work/phi/b/c.txt");
    }

    [Test]
    public async Task ParentTraversal_StillResolves()
    {
        var resolver = new WorkspacePathResolver("/work/phi");
        var resolved = resolver.Resolve("../escape.txt");
        await Assert.That(resolved).IsEqualTo("/work/escape.txt");
    }

    [Test]
    public async Task EmptyPath_ReturnsCwd()
    {
        var resolver = new WorkspacePathResolver("/work/phi");
        var resolved = resolver.Resolve("");
        await Assert.That(resolved).IsEqualTo("/work/phi");
    }
}
