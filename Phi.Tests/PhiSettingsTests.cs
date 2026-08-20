using Phi.Providers;

namespace Phi.Tests;

public class PhiSettingsTests : IDisposable
{
    private readonly string _path;

    public PhiSettingsTests()
    {
        _path = Path.Combine(
            Path.GetTempPath(), "phi-settings-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Test]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        var settings = PhiSettings.Load(_path);

        await Assert.That(settings.DefaultProvider).IsEqualTo("");
        await Assert.That(settings.DefaultModel).IsEqualTo("");
    }

    [Test]
    public async Task SaveAndLoad_RoundTrips()
    {
        PhiSettings.Save(_path, new PhiSettings
        {
            DefaultProvider = "glm",
            DefaultModel = "glm-4.6",
        });

        var loaded = PhiSettings.Load(_path);
        await Assert.That(loaded.DefaultProvider).IsEqualTo("glm");
        await Assert.That(loaded.DefaultModel).IsEqualTo("glm-4.6");
    }

    [Test]
    public async Task Load_CorruptFile_ReturnsEmpty_WithoutThrowing()
    {
        File.WriteAllText(_path, "not json {");

        var settings = PhiSettings.Load(_path);
        await Assert.That(settings.DefaultProvider).IsEqualTo("");
    }

    [Test]
    public async Task Save_CreatesParentDirectory()
    {
        var nested = Path.Combine(
            Path.GetTempPath(), "phi-settings-dir-" + Guid.NewGuid().ToString("N"), "sub", "settings.json");

        try
        {
            PhiSettings.Save(nested, new PhiSettings { DefaultProvider = "kimi" });
            await Assert.That(File.Exists(nested)).IsTrue();
            await Assert.That(PhiSettings.Load(nested).DefaultProvider).IsEqualTo("kimi");
        }
        finally
        {
            var dir = Path.GetDirectoryName(nested)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
