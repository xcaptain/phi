namespace PhiCoding.Providers;

/// <summary>
/// Built-in provider catalog: the set of vendors Phi can connect to out of
/// the box. Entries are static (no network discovery) and carry their own
/// model lists; add a new vendor here to make it appear in <c>/connect</c>.
/// </summary>
public static class ProviderCatalog
{
    public static readonly ProviderCatalogEntry DeepSeek = new()
    {
        Name = "deepseek",
        DisplayName = "DeepSeek",
        Kind = ProviderKind.Anthropic,
        BaseUrl = "https://api.deepseek.com/anthropic",
        CredentialName = "deepseek",
        Models = ["deepseek-v4-flash", "deepseek-v4-pro"],
        DefaultModel = "deepseek-v4-flash",
    };

    public static readonly ProviderCatalogEntry Glm = new()
    {
        Name = "glm",
        DisplayName = "Zhipu GLM",
        Kind = ProviderKind.OpenAICompatible,
        BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
        CredentialName = "glm",
        Models = ["glm-4.7-flash", "glm-4.7", "glm-5-turbo", "glm-5.1", "glm-5v-turbo"],
        DefaultModel = "glm-4.7-flash",
    };

    public static readonly ProviderCatalogEntry Kimi = new()
    {
        Name = "kimi",
        DisplayName = "Moonshot Kimi",
        Kind = ProviderKind.OpenAICompatible,
        BaseUrl = "https://api.moonshot.cn/v1",
        CredentialName = "kimi",
        Models = ["kimi-k2-thinking", "kimi-k2-thinking-turbo", "kimi-k2.5", "kimi-k2.6", "kimi-k2.7-code"],
        DefaultModel = "kimi-k2.7-code",
    };

    public static readonly ProviderCatalogEntry MiniMax = new()
    {
        Name = "minimax",
        DisplayName = "MiniMax",
        Kind = ProviderKind.Anthropic,
        BaseUrl = "https://api.minimaxi.com/anthropic",
        CredentialName = "minimax",
        Models = ["MiniMax-M2.7", "MiniMax-M2.7-highspeed", "MiniMax-M3"],
        DefaultModel = "MiniMax-M3",
    };

    /// <summary>All built-in providers, in <c>/connect</c> display order.</summary>
    public static readonly IReadOnlyList<ProviderCatalogEntry> All =
    [
        DeepSeek,
        Glm,
        Kimi,
        MiniMax,
    ];

    /// <summary>Returns the built-in entry by name, or null when unknown.</summary>
    public static ProviderCatalogEntry? Find(string name) =>
        All.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
