using PhiCoding.Providers;

namespace PhiCoding.Tests;

public class ProviderCatalogTests
{
    private static readonly string[] BuiltinNames = ["deepseek", "glm", "kimi", "minimax"];

    [Test]
    public async Task All_IncludesFourBuiltinVendors()
    {
        await Assert.That(ProviderCatalog.All.Select(e => e.Name))
            .IsEquivalentTo(BuiltinNames);
    }

    [Test]
    public async Task All_NamesAreUnique()
    {
        await Assert.That(ProviderCatalog.All.Select(e => e.Name).Distinct().Count())
            .IsEqualTo(ProviderCatalog.All.Count);
    }

    [Test]
    public async Task All_DefaultModelIsListed()
    {
        foreach (var entry in ProviderCatalog.All)
        {
            await Assert.That(entry.Models).Contains(entry.DefaultModel);
        }
    }

    [Test]
    public async Task All_ModelsAreNonEmptyAndUniquePerProvider()
    {
        foreach (var entry in ProviderCatalog.All)
        {
            await Assert.That(entry.Models).IsNotEmpty();
            await Assert.That(entry.Models.Distinct().Count()).IsEqualTo(entry.Models.Count);
        }
    }

    [Test]
    public async Task All_EntriesCarryConnectionBasics()
    {
        foreach (var entry in ProviderCatalog.All)
        {
            await Assert.That(entry.DisplayName).IsNotEmpty();
            await Assert.That(entry.BaseUrl).StartsWith("https://");
            await Assert.That(entry.CredentialName).IsEqualTo(entry.Name);
        }
    }

    [Test]
    public async Task All_WireFormatsMapToImplementedKinds()
    {
        // Every kind in the catalog must be one PhiProvider can construct.
        foreach (var entry in ProviderCatalog.All)
        {
            await Assert.That(Enum.IsDefined(entry.Kind)).IsTrue();
        }
    }

    [Test]
    public async Task Find_ByName_ReturnsEntry()
    {
        await Assert.That(ProviderCatalog.Find("deepseek")).IsSameReferenceAs(ProviderCatalog.DeepSeek);
    }

    [Test]
    public async Task Find_CaseInsensitive()
    {
        await Assert.That(ProviderCatalog.Find("DEEPSEEK")).IsSameReferenceAs(ProviderCatalog.DeepSeek);
    }

    [Test]
    public async Task Find_Unknown_ReturnsNull()
    {
        await Assert.That(ProviderCatalog.Find("unknown")).IsNull();
    }
}
