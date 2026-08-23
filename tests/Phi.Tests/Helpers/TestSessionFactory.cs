using Phi.Agent;
using Phi.Prompts;
using Phi.Provider;
using Phi.Providers;

namespace Phi.Tests.Helpers;

/// <summary>
/// Test-side helper for building a fully composed <see cref="Session"/>
/// the way the composition root does in production — <c>SessionEnvironment</c>
/// + <c>Session.LoadAsync</c>. Replaces the old <c>SessionFactory</c> +
/// <c>SessionConfig</c> pair the test suite used to use directly. The
/// factory built here is intentionally minimal: it just resolves the
/// injected <see cref="IPhiProvider"/> verbatim (no catalog, no env, no
/// persisted settings), so each test pins the exact provider instance
/// it cares about.
/// </summary>
internal static class TestSessionFactory
{
    /// <summary>
    /// Builds a fresh <see cref="Session"/> in <paramref name="cwd"/> with
    /// the given provider wired in. The session can submit prompts and
    /// run a turn immediately. Persistence is lazy: nothing is on disk
    /// until the first message.
    /// </summary>
    public static async Task<Session> CreateAsync(
        string cwd,
        IPhiProvider provider,
        string model = "stub-model",
        string providerName = "test")
    {
        var env = BuildEnv(provider);
        return await Session.LoadAsync(cwd, env, providerName, model);
    }

    /// <summary>
    /// Resumes an already-indexed session by id. The session's record
    /// supplies provider name and model (matching the production
    /// <c>Session.LoadAsync(resumeId: ...)</c> semantics). Pass a custom
    /// <paramref name="envFactory"/> to override compaction knobs, tools,
    /// or any other env field — needed because <see cref="SessionEnvironment"/>
    /// uses init-only properties.
    /// </summary>
    public static async Task<Session> ResumeAsync(
        string cwd,
        IPhiProvider provider,
        string id,
        Func<IPhiProvider, SessionEnvironment>? envFactory = null)
    {
        var env = envFactory?.Invoke(provider) ?? BuildEnv(provider);
        return await Session.LoadAsync(cwd, env, providerName: "", model: "", resumeId: id);
    }

    /// <summary>Default env: 128k context, 5 max turns, auto-compact on, no overrides.</summary>
    public static SessionEnvironment BuildEnv(IPhiProvider provider) =>
        new()
        {
            ProviderResolver = new FixedProviderResolver(provider),
            SystemPrompt = new SystemPromptOptions { ResolvedSystemPrompt = "test" },
            MaxTurns = 5,
            ContextWindowTokens = ContextWindow.DefaultContextWindowTokens,
            AutoCompactTokenThreshold = null,
            AutoCompactEnabled = true,
            CompactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens,
            Tools = [],
        };

    /// <summary>
    /// Resolver that hands back a fixed <see cref="IPhiProvider"/> for any
    /// name (and <see cref="NullProvider"/> when no provider was injected —
    /// matches the production fallback in <c>ProviderManager.Resolve</c>).
    /// </summary>
    private sealed class FixedProviderResolver : IProviderResolver
    {
        private readonly IPhiProvider? _provider;
        public FixedProviderResolver(IPhiProvider? provider) { _provider = provider; }
        public IPhiProvider Resolve(string providerName) => _provider ?? new NullProvider();
    }
}
