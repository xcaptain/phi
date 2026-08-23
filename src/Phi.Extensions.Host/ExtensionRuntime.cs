using Phi.Agent;
using Phi.Extensions.Events;

namespace Phi.Extensions.Host;

/// <summary>
/// Owns the loaded extensions for one session's lifetime.
/// <list type="bullet">
/// <item><see cref="DiscoverAndLoad"/> scans a list of dll paths via
/// <see cref="ExtensionLoader.Load"/>, accumulating <see cref="LoadedExtension"/>s.</item>
/// <item><see cref="Initialize"/> builds one <see cref="IPhiContext"/>
/// (shared across extensions in this session) and one <see cref="PhiApi"/>
/// per extension, then calls <see cref="IPhiExtension.Setup"/>. Tools
/// registered via <c>api.RegisterTool</c> are wrapped with
/// <see cref="HookWrappingTool"/> and folded into
/// <see cref="Phi.Agent.Harness.Tools"/> so the harness can invoke them on
/// the next turn.</item>
/// <item><see cref="Dispose"/> unloads every ALC (best-effort; release
/// of the loaded assembly is the GC's job after <c>Unload()</c>).</item>
/// </list>
/// <para>
/// One runtime per session. Composition root creates it after
/// <c>Session.LoadAsync(...)</c> returns, hands it to the TUI / Avalonia
/// shell for lifetime tracking.
/// </para>
/// <para>
/// Sprint 2 adds hook + event wiring: <see cref="HookRegistry"/> intercepts
/// tool calls / results / input; <see cref="EventDispatch"/> translates
/// <c>ISession.HarnessEvent</c> + <c>StateChanged</c> into typed
/// <c>PhiEvent</c>s for observation handlers.
/// </para>
/// </summary>
internal sealed class ExtensionRuntime : IDisposable
{
    private readonly Phi.Session _session;
    private readonly List<LoadedExtension> _extensions = [];

    private readonly HookRegistry _hooks = new();
    private EventDispatch? _eventDispatch;

    // Per-extension generation token. InvalidateAllGenerations() on reload
    // flips every one so captured PhiApi references throw (GenerationGuard).
    private readonly Dictionary<LoadedExtension, ExtensionGeneration> _generations = [];

    public ExtensionRuntime(Phi.Session session, IPhiUiBridge uiBridge)
    {
        ArgumentNullException.ThrowIfNull(session);
        UiBridge = uiBridge ?? throw new ArgumentNullException(nameof(uiBridge));
        _session = session;
    }

    /// <summary>The UI bridge the runtime forwards UI-bound calls to.</summary>
    public IPhiUiBridge UiBridge { get; }

    /// <summary>The session this runtime is bound to.</summary>
    public Phi.Session Session => _session;

    public IReadOnlyList<LoadedExtension> Extensions => _extensions;

    public IReadOnlyList<ExtensionLoadFailure> LoadResults => _loadResults;
    private readonly List<ExtensionLoadFailure> _loadResults = [];

    public IReadOnlyList<ExtensionSetupFailure> SetupResults => _setupResults;
    private readonly List<ExtensionSetupFailure> _setupResults = [];

    /// <summary>
    /// Loads every extension assembly under <paramref name="assemblyPaths"/>.
    /// Failures are recorded in <see cref="LoadResults"/> but do not stop
    /// other extensions from loading.
    /// </summary>
    public void DiscoverAndLoad(IEnumerable<string> assemblyPaths)
    {
        foreach (var path in assemblyPaths)
        {
            try
            {
                _extensions.Add(ExtensionLoader.Load(path));
            }
            catch (Exception ex)
            {
                _loadResults.Add(new ExtensionLoadFailure(path, ex));
            }
        }
    }

    /// <summary>
    /// Calls <see cref="IPhiExtension.Setup"/> on every loaded extension,
    /// in load order. Exceptions from individual extensions are caught
    /// and recorded — one bad extension can't kill the others.
    /// </summary>
    public void Initialize()
    {
        _eventDispatch = new EventDispatch(_session);

        var context = new PhiContext(_session, UiBridge);
        foreach (var ext in _extensions)
        {
            try
            {
                var gen = new ExtensionGeneration(ext.Name);
                _generations[ext] = gen;
                var api = new PhiApi(this, ext, context, gen);
                _apisForTest[ext] = api;
                ext.Instance.Setup(api);
            }
            catch (Exception ex)
            {
                _setupResults.Add(new ExtensionSetupFailure(ext, ex));
            }
        }
    }

    /// <summary>Test-only: the per-extension <see cref="IPhiApi"/> instances
    /// created during <see cref="Initialize"/>. Lets tests capture an api
    /// before <c>/reload</c> and assert it throws after invalidation.
    /// Production code never reads this (it's a test seam).</summary>
    internal IReadOnlyDictionary<LoadedExtension, IPhiApi> ApisForTest => _apisForTest;
    private readonly Dictionary<LoadedExtension, IPhiApi> _apisForTest = [];

    /// <summary>
    /// Flips every <see cref="ExtensionGeneration"/> to stale. Called by
    /// <see cref="ExtensionReloader.Reload"/>; afterwards any captured
    /// <see cref="IPhiApi"/> throws <see cref="ExtensionGenerationException"/>
    /// on action methods.
    /// </summary>
    public void InvalidateAllGenerations()
    {
        foreach (var gen in _generations.Values)
            gen.Invalidate("extension reloaded");
    }

    // ──────── Methods that PhiApi calls during Setup() / runtime ────────

    /// <summary>
    /// Adds a tool to the live harness, wrapped with the shared
    /// <see cref="HookRegistry"/> so tool_call / tool_result hooks fire
    /// around its execution. Called by <see cref="PhiApi.RegisterTool"/>.
    /// </summary>
    public void RegisterTool(LoadedExtension from, Tool tool, ToolContribution? contribution)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (contribution is not null)
        {
            if (!string.IsNullOrWhiteSpace(contribution.PromptSnippet))
                AddPromptGuideline(from, $"tool `{tool.Name}`: {contribution.PromptSnippet}");
            foreach (var g in contribution.PromptGuidelines)
                AddPromptGuideline(from, g);
        }
        var wrapped = new HookWrappingTool(tool, _hooks);
        _session.RegisterExtensionTool(wrapped);
    }

    /// <summary>
    /// Records a command registration. Sprint 2 wires it into the UI's
    /// <c>HandleInput</c> via a runtime command registry that the shell
    /// consults before its hard-coded switch. The registry is exposed to
    /// the TUI / Avalonia shell so <c>PromptInput</c> can route unknown
    /// slash commands to extensions.
    /// </summary>
    public void RegisterCommand(
        LoadedExtension from,
        string name,
        PhiCommandHandler handler,
        string description,
        IReadOnlyList<string>? aliases)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(handler);
        Commands[name] = (from, handler, description, aliases);
    }

    /// <summary>Registered slash commands, keyed by name (with leading '/' stripped).</summary>
    public Dictionary<string, (LoadedExtension Ext, PhiCommandHandler Handler, string Description, IReadOnlyList<string>? Aliases)> Commands { get; } = [];

    /// <summary>
    /// Appends a guideline to the live system prompt and surfaces it in
    /// <see cref="SessionState.SystemPrompt"/>.
    /// </summary>
    public void AddPromptGuideline(LoadedExtension from, string guideline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guideline);
        _session.AddExtensionPromptGuideline(guideline);
    }

    /// <summary>
    /// Subscribes an <c>On(eventName, handler)</c> subscription. Routes
    /// the three interception hooks to <see cref="HookRegistry"/>, other
    /// events to <see cref="EventDispatch"/>.
    /// </summary>
    public IDisposable SubscribeEvent(
        LoadedExtension from,
        string eventName,
        Func<PhiEvent, IPhiContext, ValueTask> handler)
    {
        switch (eventName)
        {
            case "tool_call":
                return _hooks.RegisterToolCall(from, handler);
            case "tool_result":
                return _hooks.RegisterToolResult(from, handler);
            case "input":
                return _hooks.RegisterInput(from, handler);
            default:
                return _eventDispatch?.Register(eventName, from, handler)
                    ?? throw new InvalidOperationException("SubscribeEvent called before Initialize");
        }
    }

    public void Dispose()
    {
        _eventDispatch?.Dispose();
        foreach (var alc in _extensions.Select(e => e.Alc).Distinct())
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
        // Drop every reference that could keep old assemblies alive: the
        // extension list, the per-extension PhiApi instances (which hold
        // the extension → assembly), and the generation tokens.
        _extensions.Clear();
        _apisForTest.Clear();
        _generations.Clear();
        _hooks.Dispose();
    }
}

/// <summary>One extension that failed to load. Path + the diagnostic exception.</summary>
internal sealed record ExtensionLoadFailure(string AssemblyPath, Exception Error);

/// <summary>One extension whose <c>Setup()</c> threw. Extension + the thrown exception.</summary>
internal sealed record ExtensionSetupFailure(LoadedExtension Extension, Exception Error);
