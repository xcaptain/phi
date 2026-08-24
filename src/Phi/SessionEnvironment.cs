using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;

namespace Phi;

/// <summary>
/// Cross-session environment: the resources that are shared across every
/// session a single app instance creates — provider resolver (so a fresh
/// session can rebuild a live provider from a stored name), system-prompt
/// options, tool registry, and the compaction knobs. Built once by the
/// composition root (one of the <c>Program.cs</c> entry points) via
/// <see cref="Default"/> and handed to every <see cref="Session"/> it
/// creates.
/// <para>
/// Composition-root territory: the record is <c>public</c> only so the
/// composition root can build it. UIs never see this type — they hold an
/// <see cref="ISession"/>.
/// </para>
/// </summary>
public sealed record SessionEnvironment
{
    public required IProviderResolver ProviderResolver { get; init; }
    public required SystemPromptOptions SystemPrompt { get; init; }
    public required int? MaxTurns { get; init; }
    public required int ContextWindowTokens { get; init; }
    public required int? AutoCompactTokenThreshold { get; init; }
    public required bool AutoCompactEnabled { get; init; }
    public required int CompactionKeepRecentTokens { get; init; }

    /// <summary>
    /// Optional pre-registered tool set for the session's harness.
    /// Sprint 2.5: the default coding tools moved out of the core into the
    /// CodingPack extension, so this is normally empty — CodingPack (and any
    /// other extension) registers its tools after <c>Session.LoadAsync</c>
    /// via <c>Session.RegisterExtensionTool</c>. A composition root that
    /// wants tools at harness-build time (e.g. for the available-tools
    /// system-prompt section) can still supply them here.
    /// </summary>
    public required IReadOnlyList<Tool>? Tools { get; init; }

    /// <summary>
    /// Optional factory building the session's extension runtime. The
    /// composition root (<c>Phi.Tui</c> / <c>Phi.Avalonia.Desktop</c>
    /// <c>Program.cs</c>) is the only thing that can reference
    /// <c>Phi.Extensions.Host</c> (Phi core cannot — that would cycle back
    /// through <c>Phi.Extensions.Host</c>'s reference to <c>Phi</c>), so this
    /// is an opaque delegate: it receives the freshly-loaded
    /// <see cref="Session"/> and returns an <see cref="IDisposable"/> handle
    /// (an <c>ExtensionRuntime</c> in practice) that <see cref="LoadAsync"/>
    /// disposes alongside the session. Carried on <see cref="SessionEnvironment"/>
    /// so it survives session switching — <see cref="NewSessionAsync"/> and
    /// <see cref="ResumeAsync"/> re-enter <see cref="LoadAsync"/> with the
    /// same <c>env</c>, so every session (not just the first) gets its
    /// compiled extensions (CodingPack etc.) re-registered automatically.
    /// </summary>
    public Func<Session, IDisposable>? ExtensionRuntimeFactory { get; init; }

    /// <summary>
    /// Async variant of <see cref="ExtensionRuntimeFactory"/> for
    /// composition roots that need to do async work (e.g. Project
    /// Trust confirm dialog) before constructing the runtime. Sprint 3b.
    /// When both are set, the async factory takes precedence in
    /// <c>LoadAsync</c>; the sync factory remains in use for
    /// <c>ReloadExtensions</c> which is intentionally sync (it fires
    /// from a slash command on the submit thread).
    /// </summary>
    public Func<Session, Task<IDisposable>>? ExtensionRuntimeFactoryAsync { get; init; }

    /// <summary>
    /// Builds a <see cref="SessionEnvironment"/> with all compaction knobs
    /// at their defaults and no custom toolset. The composition root
    /// supplies an <see cref="IProviderResolver"/> (typically the
    /// app's <c>ProviderManager</c>) and the system-prompt options; the
    /// <see cref="ContextWindow.DefaultContextWindowTokens"/> /
    /// <see cref="ContextWindow.DefaultCompactionKeepRecentTokens"/>
    /// defaults are filled in here so callers don't have to repeat them.
    /// </summary>
    public static SessionEnvironment Default(
        IProviderResolver providerResolver,
        SystemPromptOptions? systemPrompt = null,
        int? maxTurns = null,
        Func<Session, IDisposable>? extensionRuntimeFactory = null,
        Func<Session, Task<IDisposable>>? extensionRuntimeFactoryAsync = null) =>
        new()
        {
            ProviderResolver = providerResolver,
            SystemPrompt = systemPrompt ?? new SystemPromptOptions(),
            MaxTurns = maxTurns,
            ContextWindowTokens = ContextWindow.DefaultContextWindowTokens,
            AutoCompactTokenThreshold = null,
            AutoCompactEnabled = true,
            CompactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens,
            Tools = null,
            ExtensionRuntimeFactory = extensionRuntimeFactory,
            ExtensionRuntimeFactoryAsync = extensionRuntimeFactoryAsync,
        };
}
