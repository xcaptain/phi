namespace Phi.Providers;

/// <summary>
/// Wire format a provider speaks, which selects the concrete
/// <c>Phi.Provider</c> implementation to build (see
/// <see cref="ProviderManager.CreateProvider"/>).
/// </summary>
public enum ProviderKind
{
    /// <summary>OpenAI-compatible <c>/chat/completions</c> streaming.</summary>
    OpenAICompatible,

    /// <summary>Anthropic Messages API streaming.</summary>
    Anthropic,
}
