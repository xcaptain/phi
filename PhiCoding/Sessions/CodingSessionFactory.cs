using PhiAgent;
using PhiCoding.Prompts;
using PhiCoding.Providers;
using PhiCoding.Resources;

namespace PhiCoding.Sessions;

/// <summary>
/// Creates and resumes <see cref="CodingSession"/> instances. Both entry
/// points run the same construction pipeline — load resources, compose
/// tools, build the system prompt, create the harness — and differ only in
/// how the session record, transcript, and live provider are sourced:
/// <list type="bullet">
///   <item><see cref="Create"/> allocates a fresh, unpersisted session
///   using <see cref="SessionConfig.ProviderName"/>/<see cref="SessionConfig.Model"/>
///   (or the injected <see cref="IProviderResolver"/> default when no live
///   provider is supplied).</item>
///   <item><see cref="Resume"/> opens an indexed session, replays its
///   stored transcript into the harness, and rebuilds the live provider
///   from the session record's provider name (via the injected
///   <see cref="IProviderResolver"/>) so a session carrying a different
///   provider than the startup default comes back to life with the right
///   API key, base URL, and HTTP transport. The record always wins:
///   <see cref="SessionConfig.Model"/> and
///   <see cref="SessionConfig.ProviderName"/> are ignored on resume — the
///   config only supplies the environment (<see cref="SessionConfig.Cwd"/>,
///   tools, prompt, compaction knobs). An explicit live
///   <see cref="SessionConfig.Provider"/> is honored over the resolver
///   (test/DI escape hatch only).</item>
/// </list>
/// </summary>
public sealed class CodingSessionFactory
{
    private readonly IProviderResolver _resolver;

    public CodingSessionFactory(IProviderResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Creates a fresh session with a full runtime. Persistence stays lazy:
    /// the id is allocated eagerly but nothing touches disk until the first
    /// message. Uses the live <see cref="IPhiProvider"/> from
    /// <see cref="SessionConfig.Provider"/> when supplied; otherwise
    /// resolves the provider named by <see cref="SessionConfig.ProviderName"/>
    /// via the injected <see cref="IProviderResolver"/> (an empty name falls
    /// through to the default provider).
    /// </summary>
    public CodingSession Create(SessionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var session = CodingSession.Create(
            config.Cwd, config.Model, providerName: config.ProviderName);
        var provider = config.Provider ?? _resolver.Resolve(config.ProviderName);
        session.ApplyRuntime(
            BuildRuntime(config, provider, config.Model, config.ProviderName));
        return session;
    }

    /// <summary>
    /// Opens an indexed session with a full runtime. The stored transcript
    /// is loaded into the harness so the conversation continues where it
    /// left off. The session record's provider and model win; the config
    /// only supplies the environment. An explicit live
    /// <see cref="SessionConfig.Provider"/> (if any) takes precedence over
    /// the provider rebuilt from the record's name. Throws
    /// <see cref="InvalidOperationException"/> when the id is unknown.
    /// </summary>
    public CodingSession Resume(SessionConfig config, string id)
    {
        ArgumentNullException.ThrowIfNull(config);
        var session = CodingSession.Resume(id, config.Cwd);

        var provider = config.Provider ?? _resolver.Resolve(session.Record.ProviderName);
        var runtime = BuildRuntime(
            config, provider, session.Record.Model, session.Record.ProviderName);
        runtime.Harness.ReplaceMessages(session.LoadMessages());
        session.ApplyRuntime(runtime);
        return session;
    }

    /// <summary>
    /// The shared resources, tools, prompt, harness pipeline used by both
    /// <see cref="Create"/> and <see cref="Resume"/>.
    /// </summary>
    private static SessionRuntime BuildRuntime(
        SessionConfig config, IPhiProvider provider, string model, string providerName)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var contextResources = ProjectContextLoader.Load(
            new SessionResourceOptions { Cwd = config.Cwd });
        var skillResult = SkillLoader.Load(
            new SkillLoadOptions { Cwd = config.Cwd });

        var skills = skillResult.Skills;
        var contributions = config.Tools is null or { Count: 0 }
            ? new BuiltInToolProvider(config.Cwd).GetTools()
            : config.Tools.Select(WrapCustomTool).ToArray();
        var tools = contributions.Select(c => c.Tool).ToArray();

        var systemPrompt = config.SystemPrompt.ResolvedSystemPrompt
            ?? new SystemPromptBuilder().Build(new SystemPromptBuildContext
            {
                Cwd = config.Cwd,
                CurrentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Tools = contributions,
                Skills = skills,
                ContextFiles = contextResources.ContextFiles,
                Options = config.SystemPrompt,
                Shell = OperatingSystem.IsWindows()
                    ? ShellKind.PowerShell
                    : ShellKind.Bash,
            });

        var harness = new Harness(
            provider, tools, model: model,
            system: systemPrompt, maxTurns: config.MaxTurns);

        return new SessionRuntime
        {
            Harness = harness,
            Provider = provider,
            ProviderName = providerName,
            Model = model,
            SystemPrompt = systemPrompt,
            Tools = tools,
            Skills = skills,
            Config = config,
        };
    }

    private static ToolContribution WrapCustomTool(Tool tool) =>
        new()
        {
            Tool = tool,
            PromptSnippet = tool.Description,
            Source = "custom",
        };
}
