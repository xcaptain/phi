using Phi.Providers;

namespace Phi.Tests;

public class FileCredentialStoreTests : IDisposable
{
    private readonly string _path;

    public FileCredentialStoreTests()
    {
        _path = Path.Combine(
            Path.GetTempPath(), "phi-credentials-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Test]
    public async Task SetAndGet_RoundTrips()
    {
        var store = new FileCredentialStore(_path);
        store.Set("deepseek", "sk-abc");

        await Assert.That(store.Get("deepseek")).IsEqualTo("sk-abc");
    }

    [Test]
    public async Task Set_PersistsAcrossInstances()
    {
        new FileCredentialStore(_path).Set("glm", "sk-xyz");

        await Assert.That(new FileCredentialStore(_path).Get("glm")).IsEqualTo("sk-xyz");
    }

    [Test]
    public async Task Set_OverwritesExisting()
    {
        var store = new FileCredentialStore(_path);
        store.Set("kimi", "one");
        store.Set("kimi", "two");

        await Assert.That(store.Get("kimi")).IsEqualTo("two");
    }

    [Test]
    public async Task Get_Unknown_ReturnsNull()
    {
        await Assert.That(new FileCredentialStore(_path).Get("nope")).IsNull();
    }

    [Test]
    public async Task Has_ReflectsPresence()
    {
        var store = new FileCredentialStore(_path);
        await Assert.That(store.Has("minimax")).IsFalse();
        store.Set("minimax", "sk-m");
        await Assert.That(store.Has("minimax")).IsTrue();
    }

    [Test]
    public async Task Delete_RemovesKeyOnly()
    {
        var store = new FileCredentialStore(_path);
        store.Set("deepseek", "sk-a");
        store.Set("glm", "sk-b");

        store.Delete("deepseek");

        await Assert.That(store.Get("deepseek")).IsNull();
        await Assert.That(store.Get("glm")).IsEqualTo("sk-b");
    }

    [Test]
    public async Task Set_EmptyValue_Throws()
    {
        var store = new FileCredentialStore(_path);
        await Assert.That(() => store.Set("deepseek", "   ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task MissingFile_ReadsAsEmpty()
    {
        var store = new FileCredentialStore(_path);
        await Assert.That(store.Get("deepseek")).IsNull();
        await Assert.That(store.Has("deepseek")).IsFalse();
    }

    [Test]
    public async Task CorruptFile_ReadsAsEmpty_WithoutThrowing()
    {
        File.WriteAllText(_path, "not json {");
        var store = new FileCredentialStore(_path);

        await Assert.That(store.Get("deepseek")).IsNull();
    }

    [Test]
    public async Task WrittenFile_IsRestrictedToOwnerOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        var store = new FileCredentialStore(_path);
        store.Set("deepseek", "sk-secret");

        var mode = File.GetUnixFileMode(_path);
        await Assert.That(mode.HasFlag(UnixFileMode.UserRead)).IsTrue();
        await Assert.That(mode.HasFlag(UnixFileMode.UserWrite)).IsTrue();
        await Assert.That(mode.HasFlag(UnixFileMode.GroupRead)).IsFalse();
        await Assert.That(mode.HasFlag(UnixFileMode.OtherRead)).IsFalse();
    }
}
