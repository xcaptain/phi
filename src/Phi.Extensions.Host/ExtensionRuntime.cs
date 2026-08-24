using System.Reflection;
using Phi.Agent;
using Phi.Chat;
using Phi.Extensions.Events;
using Phi.Extensions.Rendering;

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
internal sealed class ExtensionRuntime : IDisposable, IExtensionRenderers
{
    private readonly Phi.Session _session;
    private readonly List<LoadedExtension> _extensions = [];

    private readonly HookRegistry _hooks = new();
    private EventDispatch? _eventDispatch;

    // Per-extension generation token. InvalidateAllGenerations() on reload
    // flips every one so captured PhiApi references throw (GenerationGuard).
    private readonly Dictionary<LoadedExtension, ExtensionGeneration> _generations = [];

    // ── Renderer registries (IExtensionRenderers) ──
    // Registered during Setup via api.RegisterToolCard /
    // api.RegisterTranscriptLineRenderer; consulted by the TUI / Avalonia
    // chat components (via Phi.Chat.IExtensionRenderers) to give extension
    // tools a custom card and extension lines a custom visual.

    /// <summary>Tool name → display descriptor (icon / title / kind).</summary>
    private readonly Dictionary<string, ToolDescriptor> _toolDescriptors = new(StringComparer.Ordinal);

    /// <summary>Tool name → card renderer (body content producer).</summary>
    private readonly Dictionary<string, ToolCardRenderer> _toolCardRenderers = new(StringComparer.Ordinal);

    /// <summary>TranscriptLine.Type → renderer (produces the host visual fragment).</summary>
    private readonly Dictionary<string, TranscriptLineRenderer> _transcriptLineRenderers = new(StringComparer.Ordinal);

    public ExtensionRuntime(Phi.Session session, IPhiUiBridge uiBridge)
    {
        ArgumentNullException.ThrowIfNull(session);
        UiBridge = uiBridge ?? throw new ArgumentNullException(nameof(uiBridge));
        _session = session;
    }

    /// <summary>The UI bridge the runtime forwards UI-bound calls to.</summary>
    public IPhiUiBridge UiBridge { get; }

    /// <summary>
    /// Capability enforcement policy for every <see cref="IPhiApi"/>
    /// action that maps to a <see cref="ExtensionCapability"/>. Default
    /// is <see cref="CapabilityEnforcementMode.Transparent"/> (v1): log
    /// mismatches to <c>~/.phi/audit.log</c> but don't block. Hosts can
    /// flip to <see cref="CapabilityEnforcementMode.Strict"/> (v1.5)
    /// globally, or a future release can do it per-extension via
    /// <c>PhiSettings</c>.
    /// </summary>
    public CapabilityEnforcementMode CapabilityEnforcement { get; set; } = CapabilityEnforcementMode.Transparent;

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
                var loaded = ExtensionLoader.Load(path);
                _extensions.Add(loaded);
                AuditLogger.Write(AuditEvent.ExtensionLoaded(loaded.Name, loaded.Version, loaded.AssemblyPath));
            }
            catch (Exception ex)
            {
                _loadResults.Add(new ExtensionLoadFailure(path, ex));
            }
        }
    }

    /// <summary>
    /// Sprint 3b Project Trust: scan <paramref name="cwd"/> for project
    /// extensions under <c>.phi/extensions/</c>, ask the user via
    /// <see cref="UiBridge"/> whether to trust them, and load the approved
    /// subset. Returns the gated assembly paths so callers (the
    /// composition root, <see cref="ExtensionReloader"/>) can reuse them
    /// for the next reload without re-scanning.
    /// </summary>
    public async Task<IReadOnlyList<string>> DiscoverAndTrustProjectExtensionsAsync(string cwd)
    {
        var paths = Phi.ProjectExtensions.DiscoverAssemblyPaths(cwd);
        if (paths.Count == 0) return [];
        var gated = await ProjectTrustGate.GateAsync(cwd, paths, UiBridge);
        foreach (var p in gated)
        {
            try
            {
                var loaded = ExtensionLoader.Load(p);
                _extensions.Add(loaded);
                AuditLogger.Write(AuditEvent.ExtensionLoaded(loaded.Name, loaded.Version, loaded.AssemblyPath));
            }
            catch (Exception ex)
            {
                _loadResults.Add(new ExtensionLoadFailure(p, ex));
            }
        }
        return gated;
    }

    /// <summary>
    /// Registers an extension compiled directly into the host (CodingPack,
    /// HelloTool, PermissionGate — anything referenced via ProjectReference).
    /// These live in the host's default ALC, so they're never unloaded by
    /// <c>/reload</c>; the metadata (name/version/description) comes from the
    /// <c>[PhiExtension]</c> attribute on <paramref name="instance"/>'s type.
    /// </summary>
    public void RegisterCompiledExtension(IPhiExtension instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var attr = instance.GetType().GetCustomAttributes<PhiExtensionAttribute>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"compile-time extension '{instance.GetType().FullName}' has no [PhiExtension] attribute");

        var loaded = new LoadedExtension(
            Name: attr.Name,
            Version: attr.Version,
            Description: attr.Description,
            EntryType: instance.GetType(),
            Instance: instance,
            AssemblyPath: "",
            Assembly: instance.GetType().Assembly,
            Alc: null,
            DeclaredCapabilities: attr.Capabilities);
        _extensions.Add(loaded);
        // Compile-time extensions are referenced via ProjectReference, so
        // AssemblyPath is empty — the audit log records "(embedded)" to
        // distinguish them from dll-loaded ones.
        AuditLogger.Write(AuditEvent.ExtensionLoaded(attr.Name, attr.Version, "(embedded)"));
    }

    /// <summary>
    /// Calls <see cref="IPhiExtension.Setup"/> on every loaded extension,
    /// in load order. Exceptions from individual extensions are caught
    /// and recorded — one bad extension can't kill the others.
    /// </summary>
    public void Initialize()
    {
        _eventDispatch = new EventDispatch(_session, UiBridge);

        var context = new PhiContext(_session, UiBridge);
        // Sprint 3: hook handlers receive the real session-aware context
        // (Ui.HasUi + PhiUiBridge) so permission-gate style hooks can ask
        // the user for confirmation via the host UI. Without this the
        // hook always sees a NullContext with HasUi=false and auto-blocks.
        _hooks.ContextProvider = () => context;
        foreach (var ext in _extensions)
        {
            try
            {
                var gen = new ExtensionGeneration(ext.Name);
                _generations[ext] = gen;
                var api = new PhiApi(this, ext, context, gen);
                _apisForTest[ext] = api;
                ext.Instance.Setup(api);
                AuditLogger.Write(AuditEvent.ExtensionSetupOk(ext.Name));
            }
            catch (Exception ex)
            {
                _setupResults.Add(new ExtensionSetupFailure(ext, ex));
                AuditLogger.Write(AuditEvent.ExtensionSetupFailed(ext.Name, ex.Message));
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
    /// Records a command registration. The registration is stored in
    /// <see cref="Commands"/>; the UI's slash dispatcher is responsible
    /// for consulting it after built-in commands fall through.
    /// <para>
    /// Sprint 3 caveat: <see cref="Commands"/> is populated but no UI
    /// dispatcher consults it yet — <c>PromptInput.HandleInput</c> only
    /// routes built-in <c>/new</c> / <c>/sessions</c> / <c>/reload</c>
    /// / <c>/exit</c>. Calling this is safe (the registration is held
    /// for future dispatchers) but extensions shouldn't rely on user-
    /// typed <c>/foo</c> hitting the handler until Sprint 4 lands the
    /// dispatcher wire.
    /// </para>
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

    // ──────── IExtensionRenderers ────────

    /// <inheritdoc />
    public bool TryGetToolDescriptor(string toolName, out ToolDescriptor descriptor)
    {
        if (_toolDescriptors.TryGetValue(toolName, out descriptor))
            return true;
        // Fall through to the built-in table (bash / read / write / edit).
        descriptor = ToolDescriptors.For(toolName);
        return false;
    }

    /// <inheritdoc />
    public bool TryGetToolCardRenderer(string toolName, out object renderer)
    {
        if (_toolCardRenderers.TryGetValue(toolName, out var r))
        {
            renderer = r;
            return true;
        }
        renderer = null!;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetTranscriptLineRenderer(string lineType, out object renderer)
    {
        if (_transcriptLineRenderers.TryGetValue(lineType, out var r))
        {
            renderer = r;
            return true;
        }
        renderer = null!;
        return false;
    }

    // ──────── Renderer registration (called by PhiApi during Setup) ────────

    /// <summary>
    /// Registers a custom tool card: a descriptor (icon / title / kind)
    /// plus an optional body renderer. Called by
    /// <see cref="PhiApi.RegisterToolCard"/>. The UI chat components
    /// consult this via <see cref="IExtensionRenderers"/> to render the
    /// tool's card.
    /// </summary>
    public void RegisterToolCard(
        LoadedExtension from,
        string toolName,
        ToolDescriptor descriptor,
        ToolCardRenderer? renderer)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(descriptor);
        _toolDescriptors[toolName] = descriptor;
        if (renderer is not null)
            _toolCardRenderers[toolName] = renderer;
    }

    /// <summary>
    /// Registers a renderer for a custom transcript line type. Called by
    /// <see cref="PhiApi.RegisterTranscriptLineRenderer"/>. The UI chat
    /// components invoke this via <see cref="IExtensionRenderers"/> when
    /// a <see cref="Phi.Chat.CustomLine"/> with a matching
    /// <see cref="Phi.Chat.CustomLine.LineType"/> arrives.
    /// </summary>
    public void RegisterTranscriptLineRenderer(
        LoadedExtension from,
        string lineType,
        TranscriptLineRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineType);
        ArgumentNullException.ThrowIfNull(renderer);
        _transcriptLineRenderers[lineType] = renderer;
    }

    /// <summary>
    /// Submits an extension-produced transcript line into the host's chat
    /// projector. Called by <see cref="PhiApi.SubmitTranscriptLine"/>.
    /// Routed through the UI bridge so the line lands on the live
    /// projector (which the host UI owns), not on a stale reference.
    /// </summary>
    public void SubmitTranscriptLine(TranscriptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        UiBridge.SubmitTranscriptLine(line);
    }

    public void Dispose()
    {
        _eventDispatch?.Dispose();
        foreach (var alc in _extensions.Select(e => e.Alc).Where(a => a is not null).Distinct())
        {
            try { alc!.Unload(); } catch { /* best-effort */ }
        }
        // Drop every reference that could keep old assemblies alive: the
        // extension list, the per-extension PhiApi instances (which hold
        // the extension → assembly), and the generation tokens.
        _extensions.Clear();
        _apisForTest.Clear();
        _generations.Clear();
        _toolDescriptors.Clear();
        _toolCardRenderers.Clear();
        _transcriptLineRenderers.Clear();
        _hooks.Dispose();
    }
}

/// <summary>One extension that failed to load. Path + the diagnostic exception.</summary>
internal sealed record ExtensionLoadFailure(string AssemblyPath, Exception Error);

/// <summary>One extension whose <c>Setup()</c> threw. Extension + the thrown exception.</summary>
internal sealed record ExtensionSetupFailure(LoadedExtension Extension, Exception Error);
