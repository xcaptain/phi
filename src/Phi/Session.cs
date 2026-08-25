using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Resources;

namespace Phi;

/// <summary>
/// Application-level session: the runtime environment for one
/// <see cref="Harness"/>. Owns the transcript (JSONL via
/// <see cref="SessionStorage"/>), the steering/follow-up message queues
/// used to inject user prompts mid-run, and the agent run loop;
/// publishes immutable <see cref="SessionState"/> snapshots via
/// <see cref="StateChanged"/> so frontends can react. Implements
/// <see cref="ISession"/> for UI binding.
/// <para>
/// One instance is exactly one conversation. Switching sessions is a
/// method on the session itself: <see cref="NewSessionAsync"/> creates a
/// fresh one (inheriting provider + model) and
/// <see cref="ResumeAsync"/> opens an indexed one by id. Both return the
/// new session and dispose this one before returning — frontends just
/// reassign their reactive binding (<c>State&lt;ISession&gt;.Value</c>
/// in the TUI, the equivalent event in the Avalonia shell). Index
/// bookkeeping is delegated to <see cref="SessionManager"/>. Persistence
/// is lazy: a fresh session holds an allocated id but writes nothing
/// until the first message (or explicit rename/touch) — see
/// <see cref="IsPersisted"/>.
/// </para>
/// </summary>
public sealed class Session : ISession
{
    private readonly SessionManager _manager;
    private readonly Lock _lock = new();

    // Mutable: resume adopts the target session's storage and record.
    private readonly SessionStorage _storage;
    private bool _persisted;

    // The cross-session environment (provider resolver, system-prompt options,
    // tool registry, compaction knobs). Null for persistence-only sessions
    // created via the test-facing static factories; non-null for sessions
    // built by the full-composition path (Session.LoadAsync), which is the
    // only kind that can answer ISession navigation requests.
    private readonly SessionEnvironment? _env;

    // Handle returned by env.ExtensionRuntimeFactory (an ExtensionRuntime in
    // practice) — opaque here because Phi core can't reference
    // Phi.Extensions.Host (see SessionEnvironment.ExtensionRuntimeFactory).
    // Disposed alongside the session so compiled extensions (CodingPack)
    // never outlive the session they were registered into.
    private IDisposable? _extensionRuntime;

    private Session(
        SessionRecord record, SessionStorage storage,
        SessionManager manager, bool persisted,
        SessionEnvironment? env = null)
    {
        Record = record;
        _storage = storage;
        _manager = manager;
        _persisted = persisted;
        _env = env;
    }

    public string Id => Record.Id;
    public string Cwd => _manager.Cwd;
    public string Model => Record.Model;

    /// <summary>
    /// The resolved system prompt currently in use by the harness.
    /// Empty string before <see cref="ApplyRuntime"/> has run.
    /// Exposed via <see cref="ISession.SystemPrompt"/> for the extension
    /// <c>IPhiContext</c> view and for diagnostics / UI display.
    /// </summary>
    public string SystemPrompt => _systemPrompt;

    /// <summary>
    /// Whether the host that built this session has a real UI attached
    /// (TUI / Avalonia). The composition root sets this to <c>true</c>
    /// after <see cref="LoadAsync"/>; tests can flip it. The
    /// <c>IPhiContext.Ui.HasUi</c> view reads from here.
    /// </summary>
    public bool HasUi { get; set; }

    public SessionRecord Record { get; private set; }
    public SessionStorage Storage => _storage;

    /// <summary>Skills available to this session, for <c>/skill:NAME</c> autocomplete.</summary>
    public IReadOnlyList<SkillDescriptor> Skills => _skills;

    /// <summary>
    /// Names of the providers available in the catalog, in display order.
    /// Reads the static <see cref="Providers.ProviderCatalog"/> so it works
    /// even on a persistence-only session (no <see cref="SessionEnvironment"/>
    /// required).
    /// </summary>
    public IReadOnlyList<string> AvailableProviders { get; } =
        [.. ProviderCatalog.All.Select(p => p.Name)];

    /// <summary>
    /// Whether this session has been written to disk yet. Fresh sessions
    /// start unpersisted; the first <see cref="AppendMessage"/> (or
    /// <see cref="Touch"/>/<see cref="Rename"/>) writes the index record.
    /// </summary>
    public bool IsPersisted => _persisted;

    // ──────── Factories ────────

    /// <summary>
    /// Returns the project's stable default session, creating and indexing
    /// it on first use.
    /// </summary>
    public static Session GetOrCreateDefault(string cwd, string model, string providerName = "")
    {
        var manager = new SessionManager(cwd);
        var record = manager.GetOrCreateDefaultSession(model, providerName);
        return new(record, OpenStorage(manager, record.Id), manager, persisted: true);
    }

    /// <summary>
    /// Creates a fresh session without writing anything to disk. The id is
    /// allocated eagerly; the transcript file and index record appear on
    /// the first persisted message. <paramref name="env"/> is the
    /// cross-session context (provider resolver, system-prompt options,
    /// compaction knobs); null for persistence-only test sessions that
    /// don't need to navigate.
    /// </summary>
    public static Session Create(string cwd, string model, string? title = null, string providerName = "", SessionEnvironment? env = null)
    {
        var manager = new SessionManager(cwd);
        var record = manager.PrepareSession(model, title, providerName);
        return new(record, OpenStorage(manager, record.Id), manager, persisted: false, env: env);
    }

    /// <summary>
    /// Opens an already-indexed session. <paramref name="env"/> is the
    /// cross-session context (provider resolver, system-prompt options,
    /// compaction knobs); null for persistence-only test sessions that
    /// don't need to navigate. Throws when the id is unknown.
    /// </summary>
    public static Session Resume(string id, string cwd, SessionEnvironment? env = null)
    {
        var manager = new SessionManager(cwd);
        var record = manager.GetSession(id);
        return new(record, OpenStorage(manager, id), manager, persisted: true, env: env);
    }

    private static SessionStorage OpenStorage(SessionManager manager, string id) =>
        new(manager.SessionFileFor(id));

    /// <summary>
    /// Full-composition entry point. Builds a fresh or resumed
    /// <see cref="Session"/> with a runtime (harness + provider + system
    /// prompt + tools) wired up — the path the composition root
    /// (<c>Program.cs</c>) takes for both UIs, and the path
    /// <see cref="NewSessionAsync"/> / <see cref="ResumeAsync"/> re-enter
    /// when the user navigates inside the chat. Replaces the old
    /// <c>SessionFactory</c>.
    /// <para>
    /// On a fresh <paramref name="resumeId"/>-less call the new session
    /// is unpersisted (id allocated eagerly, nothing on disk until the
    /// first message) and uses <paramref name="providerName"/> /
    /// <paramref name="model"/>. On a non-null <paramref name="resumeId"/>
    /// the session is rebuilt from its on-disk record; the record's
    /// <c>ProviderName</c> and <c>Model</c> always win, and
    /// <paramref name="providerName"/> / <paramref name="model"/> are
    /// ignored — the environment only supplies the cwd, tools, prompt
    /// options, and compaction knobs.
    /// </para>
    /// </summary>
    public static async Task<Session> LoadAsync(
        string cwd,
        SessionEnvironment env,
        string providerName,
        string model,
        string? resumeId = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (resumeId is { Length: > 0 } id)
        {
            var session = Resume(id, cwd, env);
            var provider = env.ProviderResolver.Resolve(session.Record.ProviderName);
            var runtime = BuildRuntime(env, provider, session.Record.Model, session.Record.ProviderName, cwd);
            runtime.Harness.ReplaceMessages(session.LoadMessages());
            session.ApplyRuntime(runtime);
            await session.AttachExtensionRuntimeAsync();
            return session;
        }
        else
        {
            var session = Create(cwd, model, providerName: providerName, env: env);
            var provider = env.ProviderResolver.Resolve(providerName);
            var runtime = BuildRuntime(env, provider, model, providerName, cwd);
            session.ApplyRuntime(runtime);
            await session.AttachExtensionRuntimeAsync();
            return session;
        }
    }

    /// <summary>
    /// Calls <see cref="SessionEnvironment.ExtensionRuntimeFactory"/> (when
    /// the env exposes one) and stores the returned runtime handle for
    /// <see cref="Dispose"/>. Called from <see cref="LoadAsync"/> and
    /// <see cref="ReloadExtensions"/>.
    /// </summary>
    private void AttachExtensionRuntime()
    {
        _extensionRuntime = _env?.ExtensionRuntimeFactory?.Invoke(this);
    }

    /// <summary>
    /// Async variant of <see cref="AttachExtensionRuntime"/>: lets the
    /// composition root run async work (e.g. Project Trust confirm
    /// dialog) before constructing the runtime. Sprint 3b.
    /// </summary>
    private async Task AttachExtensionRuntimeAsync()
    {
        if (_env?.ExtensionRuntimeFactoryAsync is { } asyncFactory)
            _extensionRuntime = await asyncFactory(this);
        else
            _extensionRuntime = _env?.ExtensionRuntimeFactory?.Invoke(this);
    }

    /// <summary>
    /// Disposes the current extension runtime (unloading ALCs, clearing
    /// hooks + event dispatch, invalidating captured <c>IPhiApi</c>
    /// generations) and asks <see cref="SessionEnvironment.ExtensionRuntimeFactory"/>
    /// for a fresh one. Compiled extensions registered through the factory
    /// (e.g. CodingPack in the default composition root) re-register
    /// automatically because the factory rebuilds them from scratch —
    /// this is what makes <c>/reload</c> not lose the four coding tools.
    /// No-op when the env has no factory (persistence-only sessions).
    /// Throws <see cref="InvalidOperationException"/> when the session has
    /// no runtime yet (call <see cref="LoadAsync"/> first).
    /// </summary>
    public void ReloadExtensions()
    {
        // Env is the precondition: persistence-only sessions have no env
        // and therefore no runtime to reload. Runtime check is implicit —
        // an env-bearing session always has a runtime (LoadAsync attaches
        // it as the last step), so the env check is enough.
        ThrowIfNoEnv();
        // Drop every tool the previous runtime added to the harness BEFORE
        // disposing its ALCs — otherwise the harness keeps strong references
        // to the now-unloaded extension assembly, defeating collectible-ALC
        // collection (same rationale as ExtensionReloader.Reload).
        RemoveExtensionTools();
        // Dispose first (invalidates generations, unloads ALCs, clears
        // hooks + EventDispatch). AttachExtensionRuntime then builds a
        // brand-new runtime — its registered tools replace the old ones in
        // the harness via the factory's RegisterCompiledExtension calls.
        _extensionRuntime?.Dispose();
        AttachExtensionRuntime();
    }

    /// <summary>
    /// Loads project context, skills, tool contributions; builds the system
    /// prompt; constructs the harness and packages everything as a
    /// <see cref="SessionRuntime"/>. Shared by the fresh and resume paths in
    /// <see cref="LoadAsync"/>.
    /// </summary>
    private static SessionRuntime BuildRuntime(
        SessionEnvironment env, IPhiProvider provider,
        string model, string providerName, string cwd)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var contextResources = ProjectContextLoader.Load(
            new SessionResourceOptions { Cwd = cwd });
        var skillResult = SkillLoader.Load(
            new SkillLoadOptions { Cwd = cwd });

        var skills = skillResult.Skills;
        // Sprint 2.5: the built-in tools moved out of the core into the
        // CodingPack extension. The harness starts with whatever the
        // composition root injected via env.Tools (usually empty — CodingPack
        // registers its tools post-ApplyRuntime via Session.RegisterExtensionTool).
        var contributions = env.Tools is null or { Count: 0 }
            ? []
            : env.Tools.Select(WrapCustomTool).ToArray();
        var tools = contributions.Select(c => c.Tool).ToArray();

        var systemPrompt = env.SystemPrompt.ResolvedSystemPrompt
            ?? new SystemPromptBuilder().Build(new SystemPromptBuildContext
            {
                Cwd = cwd,
                CurrentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Tools = contributions,
                Skills = skills,
                ContextFiles = contextResources.ContextFiles,
                Options = env.SystemPrompt,
                Shell = OperatingSystem.IsWindows()
                    ? ShellKind.PowerShell
                    : ShellKind.Bash,
            });

        var harness = new Harness(
            provider, tools, model: model,
            system: systemPrompt, maxTurns: env.MaxTurns);

        return new SessionRuntime
        {
            Harness = harness,
            Provider = provider,
            ProviderName = providerName,
            Model = model,
            SystemPrompt = systemPrompt,
            Tools = tools,
            Skills = skills,
            Environment = env,
        };
    }

    private static ToolContribution WrapCustomTool(Tool tool) =>
        new()
        {
            Tool = tool,
            PromptSnippet = tool.Description,
            Source = "custom",
        };

    // ──────── Persistence ────────

    /// <summary>
    /// Appends a message to the transcript. The first append on a fresh
    /// session also writes the index record (lazy persistence).
    /// </summary>
    public void AppendMessage(IAgentMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = SessionEntryConverter.FromAgentMessage(message);
        lock (_lock)
        {
            _storage.Append(entry);
            TouchRecord();
        }
        MarkPersisted();
    }

    /// <summary>
    /// Appends a namespaced extension entry (<c>IPhiApi.AppendEntryAsync</c>)
    /// to the session's JSONL without turning it into conversation content.
    /// The entry is persisted for the extension's own bookkeeping and
    /// replayed on resume, but <see cref="LoadMessages"/> filters it out so
    /// it never enters the harness / model context.
    /// </summary>
    public void AppendExtensionEntry(
        string ns,
        System.Text.Json.Nodes.JsonNode? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ns);
        var entry = new ExtensionSessionEntry(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ns, data);
        lock (_lock)
        {
            _storage.Append(entry);
            TouchRecord();
        }
        MarkPersisted();
    }

    /// <summary>
    /// Injects an extension-produced custom message
    /// (<c>IPhiApi.SubmitCustomMessage</c>): persists it, appends it to the
    /// live harness so the model sees it on the next turn, and surfaces it
    /// in <see cref="State.Messages"/> for the transcript to render via the
    /// registered message renderer.
    /// </summary>
    public void InjectCustomMessage(CustomMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.CustomType);
        ThrowIfNoRuntime();

        _harness!.AppendMessage(message);
        AppendMessage(message);

        var messages = _harness.Messages;
        UpdateState(s => s with
        {
            Messages = [.. messages],
            ContextUsedTokens = EstimateContextUsage(messages),
        });
        // Mirror the projector's resume edge so the custom line appears
        // immediately even though it didn't come through a HarnessEvent.
        _lastMessageCount = messages.Count;
    }

    public IReadOnlyList<IAgentMessage> LoadMessages()
    {
        lock (_lock)
            return [.. _storage.ReadAll()
                // Extension entries are bookkeeping (AppendEntryAsync), not
                // conversation history — they never round-trip to a message.
                .Where(e => e is not ExtensionSessionEntry)
                .Select(SessionEntryConverter.ToAgentMessage)];
    }

    public void Touch()
    {
        lock (_lock) TouchRecord();
        MarkPersisted();
    }

    public void Rename(string? newTitle)
    {
        lock (_lock)
        {
            Record = Record with { Title = newTitle };
            _manager.Upsert(Record);
            _persisted = true;
        }
        MarkPersisted();
    }

    /// <summary>Bumps <c>UpdatedAt</c>, upserts the index, marks persisted.</summary>
    private void TouchRecord()
    {
        Record = Record with
        {
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        _manager.Upsert(Record);
        _persisted = true;
    }

    // ──────── ISession explicit interface bridge ────────

    void ISession.RenameSession(string? title) => Rename(title);

    Task<string> ISession.LoadSkillAsync(string name, string? prompt)
        => LoadSkillAsync(name, prompt);

    // ──────── Runtime (reactive engine state) ────────

    private Harness? _harness;
    private IPhiProvider? _provider;
    private string _providerName = "";
    private readonly Queue<UserMessage> _steeringQueue = new();
    private readonly Queue<UserMessage> _followUpQueue = new();
    private string _runtimeModel = "";
    private string _systemPrompt = "";
    private IReadOnlyList<Tool> _tools = [];
    private IReadOnlyList<SkillDescriptor> _skills = [];
    private CancellationTokenSource? _runCts;
    private Task? _currentRunTask;
    private int _lastMessageCount;
    private bool _autoNamed;
    private SessionState _state = SessionState.Empty;
    private bool _runtimeStarted;
    private int _contextWindowTokens = ContextWindow.DefaultContextWindowTokens;
    private int? _autoCompactThreshold;
    private bool _autoCompactEnabled = true;
    private int _compactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens;
    // Cumulative read/modified files carried forward from the most recent
    // compaction. Merged (not overwritten) on every new compaction so the
    // summary prompt's <read-files>/<modified-files> sections grow across
    // the session's lifetime rather than only reflecting the latest cut.
    private CompactionDetails _lastCompactionDetails = CompactionDetails.Empty;
    // Cumulative token usage of every summary LLM call. Added to the
    // SessionStats reported to the UI so the session's billed totals
    // include summarization work, not just assistant turns.
    private Usage _accumulatedSummaryUsage = new();

    /// <summary>Fired on every <see cref="State"/> change.</summary>
    public event Action<SessionState>? StateChanged;

    /// <summary>Fired for streaming harness events (text deltas, tool calls).</summary>
    public event Action<HarnessEvent>? HarnessEvent;

    /// <summary>Latest immutable state.</summary>
    public SessionState State => _state;

    /// <summary>
    /// Binds a fully-built <see cref="SessionRuntime"/> to this session:
    /// harness, provider, model, resolved system prompt, tool set, and the
    /// compaction knobs derived from the runtime's config. Must be called
    /// once before any action methods. Unlike the old split between runtime
    /// startup and config application, the initial
    /// <see cref="SessionState"/> is built with the resolved prompt and
    /// tools already in place, so the initial context estimate is correct.
    /// </summary>
    internal void ApplyRuntime(SessionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _harness = runtime.Harness;
        _provider = runtime.Provider;
        _providerName = runtime.ProviderName;
        _runtimeModel = runtime.Model;
        _systemPrompt = runtime.SystemPrompt;
        _tools = runtime.Tools;
        _skills = runtime.Skills;
        _contextWindowTokens = runtime.Environment.ContextWindowTokens;
        _autoCompactEnabled = runtime.Environment.AutoCompactEnabled;
        _compactionKeepRecentTokens = runtime.Environment.CompactionKeepRecentTokens;
        _autoCompactThreshold = runtime.Environment.AutoCompactTokenThreshold
            ?? ContextWindow.AutoCompactionThresholdForContextWindow(_contextWindowTokens);
        _lastMessageCount = runtime.Harness.Messages.Count;
        _runtimeStarted = true;

        // Resume paths rehydrate the compaction accumulators from disk;
        // fresh Create paths have no history to restore (_persisted false).
        if (_persisted)
        {
            RestoreCompactionHistoryFromStorage();
        }

        var messages = runtime.Harness.Messages.ToList();
        _state = new SessionState
        {
            Messages = messages,
            SessionId = Id,
            Model = _runtimeModel,
            ProviderName = _providerName,
            SessionTitle = Record.Title,
            IsPersisted = _persisted,
            Stats = SessionStatsCalculator.WithAddedUsage(
                SessionStatsCalculator.Calculate(messages),
                _accumulatedSummaryUsage),
            ContextUsedTokens = EstimateContextUsage(messages),
            AutoCompactThreshold = _autoCompactThreshold,
        };
    }

    /// <summary>
    /// Replaces the in-memory message list (harness + flush watermark)
    /// without touching storage. Used by <see cref="CompactionStorage"/>
    /// after it has rewritten the file; the caller guarantees disk and
    /// memory are about to agree.
    /// </summary>
    internal void ReplaceMessagesForCompaction(IReadOnlyList<IAgentMessage> messages)
    {
        _harness!.ReplaceMessages(messages);
        _lastMessageCount = messages.Count;
        UpdateState(s => s with
        {
            Messages = [.. messages],
            Stats = SessionStatsCalculator.WithAddedUsage(
                SessionStatsCalculator.Calculate(messages),
                _accumulatedSummaryUsage),
            ContextUsedTokens = EstimateContextUsage(messages),
        });
    }

    // ──────── Extension hooks (Sprint 1+) ────────

    /// <summary>
    /// Registers a tool added by an extension after <see cref="ApplyRuntime"/>.
    /// The tool is appended to the live harness's tool list immediately
    /// (via <see cref="Phi.Agent.Harness.AddTool"/>) so the next turn picks
    /// it up; the cached message count is updated so the just-added tool
    /// doesn't trigger a spurious transcript delta.
    /// </summary>
    /// <remarks>
    /// Sprint 1 only supports adding tools. Removing tools (for /reload
    /// unloading in Sprint 2) requires rebuilding the harness because the
    /// underlying <see cref="Phi.Agent.Harness"/> doesn't expose
    /// <c>RemoveTool</c>.
    /// </remarks>
    private readonly List<Phi.Agent.Tool> _extensionTools = [];

    public void RegisterExtensionTool(Phi.Agent.Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ThrowIfNoRuntime();
        _harness!.AddTool(tool);
        _extensionTools.Add(tool);
        _lastMessageCount = _harness.Messages.Count;
        _tools = [.. _harness.Tools];
    }

    /// <summary>
    /// Removes every extension-registered tool from the live harness
    /// (and forgets them). Called on extension <c>/reload</c> before the
    /// new extension set re-registers, so old tools don't keep strong
    /// references to the unloaded extension's assembly.
    /// </summary>
    public void RemoveExtensionTools()
    {
        if (_harness is null) return;
        var removed = _extensionTools.ToArray();
        foreach (var t in removed)
            _harness.RemoveTools(x => ReferenceEquals(x, t));
        _extensionTools.Clear();
        _lastMessageCount = _harness.Messages.Count;
        _tools = [.. _harness.Tools];
    }

    /// <summary>
    /// Appends a guideline string to the live system prompt and surfaces
    /// the updated prompt in <see cref="State.SystemPrompt"/>.
    /// </summary>
    /// <remarks>
    /// Sprint 1 implementation: simple string concatenation. The harness's
    /// own prompt (the one the model actually sees) is built once in
    /// <see cref="ApplyRuntime"/>; updates here show up in the UI's
    /// prompt-display path but the model won't see the new guideline
    /// until Sprint 2's full prompt-rebuild pipeline lands.
    /// </remarks>
    public void AddExtensionPromptGuideline(string guideline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guideline);
        ThrowIfNoRuntime();
        _systemPrompt = (_systemPrompt ?? "") + "\n- " + guideline;
        UpdateState(s => s with { SystemPrompt = _systemPrompt });
    }

    private int EstimateContextUsage(IReadOnlyList<IAgentMessage> messages) =>
        ContextWindow.EstimateContextUsage(_systemPrompt, messages, _tools);

    /// <summary>
    /// Rehydrates the cumulative compaction metadata from the on-disk
    /// transcript: the most recent <see cref="CompactionSessionEntry"/>'s
    /// <see cref="CompactionDetails"/> (carrying the running read/modified
    /// file list) and the sum of every entry's <see cref="Usage"/> (so the
    /// session's reported totals include historical summarization cost).
    /// <para>
    /// CompactionSessionEntry is materialized as a plain UserMessage by
    /// <see cref="SessionEntryConverter"/>, so the message list alone can't
    /// surface these fields; we walk the raw <see cref="SessionStorage"/>
    /// entries here.
    /// </para>
    /// </summary>
    private void RestoreCompactionHistoryFromStorage()
    {
        var rawEntries = _storage.ReadAll().ToList();
        var restoredDetails = CompactionDetails.Empty;
        var restoredSummaryUsage = new Usage();
        foreach (var entry in rawEntries)
        {
            if (entry is not CompactionSessionEntry c) continue;
            if (c.Details is not null) restoredDetails = c.Details;
            if (c.Usage is not null)
                restoredSummaryUsage = AddUsage(restoredSummaryUsage, c.Usage);
        }
        _lastCompactionDetails = restoredDetails;
        _accumulatedSummaryUsage = restoredSummaryUsage;
    }

    // ──────── Runtime actions ────────

    /// <summary>
    /// Switches the active model within the current provider. Applies to the
    /// next run only — an in-flight run keeps the model it started with, and
    /// the provider's HTTP transport is untouched (the model is a per-request
    /// parameter). Persists the change to the session record and state.
    /// </summary>
    public void SwitchModel(string model)
    {
        ThrowIfNoRuntime();
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (model == _runtimeModel) return;

        _runtimeModel = model;
        _harness!.Model = model;
        Record = Record with { Model = model };
        TouchRecord();
        UpdateState(s => s with { Model = model });
    }

    /// <summary>
    /// Switches to a different provider (and its default model). The session
    /// takes ownership of <paramref name="provider"/>: the previous provider
    /// is disposed (releasing its HTTP transport) unless it is the very same
    /// instance. Applies to the next run only; an in-flight run keeps the
    /// provider it started with. Persists the change to the session record
    /// and state.
    /// </summary>
    public void SwitchProvider(IPhiProvider provider, string providerName, string model)
    {
        ThrowIfNoRuntime();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var previous = _provider;
        _provider = provider;
        _providerName = providerName;
        _runtimeModel = model;
        _harness!.Provider = provider;
        _harness!.Model = model;

        Record = Record with { Model = model, ProviderName = providerName };
        _manager.Upsert(Record);
        _persisted = true;

        UpdateState(s => s with { Model = model, ProviderName = providerName });

        if (previous is not null && !ReferenceEquals(previous, provider))
            previous.Dispose();
    }

    public void SubmitPrompt(string text)
    {
        ThrowIfNoRuntime();
        if (_state.IsRunning) return;
        _currentRunTask = RunAgentCoreAsync(text);
    }

    public void Cancel()
    {
        ThrowIfNoRuntime();
        _runCts?.Cancel();
    }

    /// <summary>
    /// Waits for any in-flight run to fully settle (cancel + finish). Used
    /// by <see cref="NewSessionAsync"/> / <see cref="ResumeAsync"/> before
    /// disposing this session on navigation, so the run's finally block
    /// (flush, state reset) completes before the provider is released.
    /// Awaiting a completed or absent task is a no-op.
    /// </summary>
    internal async Task WaitUntilIdleAsync()
    {
        var run = _currentRunTask;
        if (run is not null)
        {
            _runCts?.Cancel();
            await run;
        }
    }

    /// <summary>
    /// Loads a skill's <c>SKILL.md</c> into the conversation and starts a run
    /// so the model acts on the skill immediately — matching pi, where
    /// <c>/skill:NAME</c> submits the skill block as the user prompt and
    /// executes the skill (bare invocation runs it; a trailing prompt is
    /// fused into the same user message after the block). The block is the
    /// pi-style <c>&lt;skill&gt;</c> XML format with the frontmatter stripped,
    /// so <see cref="Resources.SkillInvocation.TryParse"/> can parse it back
    /// and the UI can render it as a collapsible card. Returns the submitted
    /// content so frontends can render it. Throws
    /// <see cref="InvalidOperationException"/> when the skill name is unknown
    /// or a run is already in progress.
    /// </summary>
    public async Task<string> LoadSkillAsync(string name, string? prompt)
    {
        ThrowIfNoRuntime();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_state.IsRunning)
            throw new InvalidOperationException(
                "Cannot load a skill while a run is in progress. Wait for it to finish or press Esc to cancel.");

        var skill = _skills.FirstOrDefault(s =>
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"Unknown skill: {name}, available skills: {string.Join(", ", _skills.Select(s => s.Name))}");

        var skillDir = Path.GetDirectoryName(skill.AbsolutePath) ?? "";
        var body = SkillFrontmatterParser.StripFrontmatter(
            await File.ReadAllTextAsync(skill.AbsolutePath));
        var content = SkillInvocation.Build(skill.Name, skill.AbsolutePath, skillDir, body, prompt);

        SubmitPrompt(content);
        return content;
    }

    public void EnqueueSteering(UserMessage message)
    {
        ThrowIfNoRuntime();
        lock (_lock) _steeringQueue.Enqueue(message);
        UpdateQueueCount();
    }

    public void EnqueueFollowUp(UserMessage message)
    {
        ThrowIfNoRuntime();
        lock (_lock) _followUpQueue.Enqueue(message);
        UpdateQueueCount();
    }

    // ──────── Navigation ────────

    /// <summary>
    /// Creates a fresh session in <paramref name="cwd"/> (or the current
    /// session's cwd when null) inheriting the current session's provider
    /// and model. The new session is returned; this session is disposed
    /// before returning. Frontends just reassign their reactive binding
    /// (the TUI's <c>State&lt;ISession&gt;.Value = next</c>, the Avalonia
    /// shell's equivalent).
    /// </summary>
    public async Task<ISession> NewSessionAsync(string? cwd = null)
    {
        ThrowIfNoEnv();
        var newCwd = cwd ?? Cwd;
        // Cancel + await any in-flight run so its messages flush to the
        // outgoing session's file before we hand back the new one.
        await WaitUntilIdleAsync();
        var next = await LoadAsync(newCwd, _env!, _providerName, _runtimeModel);
        Dispose();
        return next;
    }

    /// <summary>
    /// Resumes the indexed session identified by <paramref name="sessionId"/>.
    /// Resolves the session's own cwd from its record so cross-workspace
    /// resume works (the desktop shell lists sessions across every project;
    /// the record's cwd is the source of truth). The new session is
    /// returned; this session is disposed before returning. Throws
    /// <see cref="InvalidOperationException"/> when the id is unknown.
    /// </summary>
    public async Task<ISession> ResumeAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new InvalidOperationException("Cannot resume an empty session id.");
        ThrowIfNoEnv();
        var record = WorkspaceSessionStore.FindSession(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found");
        await WaitUntilIdleAsync();
        var next = await LoadAsync(
            record.Cwd, _env!, record.ProviderName, record.Model,
            resumeId: sessionId);
        Dispose();
        return next;
    }

    /// <summary>
    /// Indexed sessions of this session's project, last touched within
    /// <paramref name="days"/> days, newest first. Backed by the same
    /// <see cref="SessionManager"/> the session itself uses, so a freshly
    /// persisted session appears on the next call. No <see cref="SessionEnvironment"/>
    /// required.
    /// </summary>
    public IReadOnlyList<SessionRecord> ListRecent(int days = 7) =>
        new SessionManager(Cwd).ListSessions(days);

    private void ThrowIfNoEnv()
    {
        if (_env is null)
            throw new InvalidOperationException(
                "This session has no SessionEnvironment — it was created by the persistence-only factory " +
                "(Session.Create / Session.Resume) and cannot navigate. Use Session.LoadAsync to build a " +
                "fully composed session.");
    }

    // ──────── Internal engine loop ────────

    private async Task RunAgentCoreAsync(string prompt)
    {
        // A new run clears the previous LastError so the status bar can
        // restore its normal display (and a fresh failure of the same kind
        // leaves a new transcript record). Without this, LastError stays
        // sticky forever and every later StateChanged re-routes the error.
        UpdateState(s => s with { IsRunning = true, LastError = null });

        // Auto-name on the first message only (_autoNamed guards
        // subsequent runs). Fire-and-forget — if the LLM call fails the
        // fallback still produces a title from the prompt text.
        _ = TryAutoNameSessionAsync(prompt);

        try
        {
            _runCts = new CancellationTokenSource();

            // Auto-compact before adding the new prompt, so we measure the
            // pre-prompt context size and the user message itself always
            // lands in the recent (kept) suffix.
            await TryAutoCompactAsync();

            await RunOnceAsync(prompt);
        }
        catch (Exception ex)
        {
            UpdateState(s => s with { LastError = ex.Message });
        }
        finally
        {
            FlushNewMessages();
            _runCts?.Dispose();
            _runCts = null;
            UpdateState(s => s with { IsRunning = false });
            _currentRunTask = null;
        }
    }

    private async Task RunOnceAsync(string prompt)
    {
        var harness = _harness!;
        AssistantMessage? lastAssistant = null;

        await foreach (var ev in harness.RunAsync(
            prompt,
            getSteeringMessages: () => DrainQueueLocked(_steeringQueue),
            getFollowUpMessages: () => DrainQueueLocked(_followUpQueue),
            cancellationToken: _runCts!.Token))
        {
            HarnessEvent?.Invoke(ev);

            // Persist after every event: each completed message lands
            // on disk immediately, so a crash mid-run loses at most
            // the in-flight message.
            FlushNewMessages();

            if (ev is TurnEndEvent te)
            {
                lastAssistant = te.FinalMessage;
                UpdateState(s => s with
                {
                    Messages = [.. harness.Messages],
                    Turn = s.Turn + 1,
                    Stats = SessionStatsCalculator.WithAddedUsage(
                        SessionStatsCalculator.Calculate(harness.Messages),
                        _accumulatedSummaryUsage),
                    ContextUsedTokens = EstimateContextUsage(harness.Messages),
                    // Terminal provider failures arrive as a normal
                    // TurnEndEvent carrying StopReason=Error (the loop no
                    // longer throws) — surface ErrorMessage so the status
                    // router can classify and display it.
                    LastError = te.FinalMessage.StopReason == StopReasons.Error
                        ? te.FinalMessage.ErrorMessage ?? te.FinalMessage.Text
                        : s.LastError,
                });
            }
        }

        // Overflow: the turn ended with a context-overflow error. Run a
        // proactive compaction now so the user's NEXT prompt has room.
        // The failed turn's messages stay in history so the user can see
        // what happened.
        if (lastAssistant is not null
            && lastAssistant.StopReason == StopReasons.Error
            && OverflowDetector.IsOverflow(lastAssistant.ErrorMessage))
        {
            await TryAutoCompactAsync(force: true);
        }
    }

    private async Task TryAutoCompactAsync(bool force = false)
    {
        if (!_runtimeStarted || _harness is null || _provider is null) return;
        if (!force && !_autoCompactEnabled) return;

        var messages = _harness.Messages;
        if (messages.Count < 2) return;

        var currentUsage = EstimateContextUsage(messages);
        if (!force && _autoCompactThreshold is { } threshold && currentUsage <= threshold)
            return;

        var plan = CompactionPlanner.Build(messages, _compactionKeepRecentTokens);
        if (plan is null) return;

        var tokensBefore = currentUsage;

        // File ops accumulate across compactions: extract from the dropped
        // span (history + turn prefix if split), then merge into whatever
        // the previous compaction already knew about. The merged result is
        // both fed into the summary prompt as <read-files>/<modified-files>
        // context AND persisted on the new CompactionSessionEntry so the
        // next compaction inherits it.
        var newOps = FileOpsExtractor.Extract(plan.MessagesToSummarize);
        if (plan.IsSplitTurn)
        {
            newOps = newOps.Merge(FileOpsExtractor.Extract(plan.TurnPrefixMessages));
        }
        var mergedDetails = _lastCompactionDetails.Merge(newOps);

        CompactionSummarizer.SummaryResult result;
        try
        {
            result = await CompactionSummarizer.GenerateAsync(
                _provider, _runtimeModel,
                plan.MessagesToSummarize,
                turnPrefixMessages: plan.IsSplitTurn ? plan.TurnPrefixMessages : null,
                previousDetails: mergedDetails,
                cancellationToken: _runCts?.Token ?? default);
        }
        catch (Exception ex)
        {
            UpdateState(s => s with { LastError = $"Compaction failed: {ex.Message}" });
            return;
        }

        await CompactionStorage.RewriteAsync(
            this, plan, result.Text, tokensBefore,
            mergedDetails, result.Usage,
            _runCts?.Token ?? default);

        // Update accumulators BEFORE the post-check so a failed compaction
        // (didn't actually shrink) still bumps usage — the LLM call was
        // made and the tokens were spent.
        _lastCompactionDetails = mergedDetails;
        _accumulatedSummaryUsage = AddUsage(_accumulatedSummaryUsage, result.Usage);

        // Post-check: compaction must actually reduce context size; if it
        // didn't, leave the larger history alone rather than thrash. The
        // accumulators above stay applied — the next prompt will see the
        // original prefix (still on disk) and the cumulative usage.
        var afterUsage = EstimateContextUsage(_harness.Messages);
        if (!force && _autoCompactThreshold is { } th && afterUsage >= th)
        {
            UpdateState(s => s with
            {
                LastError = "Compaction did not reduce context size; keeping original history",
            });
            return;
        }

        UpdateState(s => s with
        {
            ContextUsedTokens = afterUsage,
            Stats = SessionStatsCalculator.WithAddedUsage(
                SessionStatsCalculator.Calculate(_harness.Messages),
                _accumulatedSummaryUsage),
        });
    }

    private static Usage AddUsage(Usage a, Usage b)
    {
        // Defensive: a null Usage carries no usage; b may be null if the
        // provider never emitted ProviderResponseEndEvent with usage.
        if (b is null) return a;
        return new Usage
        {
            Input = a.Input + b.Input,
            Output = a.Output + b.Output,
            CacheRead = a.CacheRead + b.CacheRead,
            CacheWrite = a.CacheWrite + b.CacheWrite,
            CacheWrite1h = a.CacheWrite1h + b.CacheWrite1h,
            Reasoning = a.Reasoning + b.Reasoning,
            TotalTokens = a.TotalTokens + b.TotalTokens,
        };
    }

    private async Task TryAutoNameSessionAsync(string firstMessage)
    {
        if (_autoNamed) return;
        _autoNamed = true;

        string? name = null;
        try
        {
            var prompt = $"Create a concise session name in at most 4 words for this user message:\n\n{firstMessage}";
            name = "";
            var msgs = new List<IAgentMessage> { new UserMessage { Content = prompt } };

            await foreach (var ev in _provider!.StreamResponseAsync(
                _runtimeModel, "You write concise session names.", msgs, [], default))
            {
                if (ev is ProviderTextDeltaEvent t) name += t.Delta;
            }
        }
        catch
        {
            name = null;
        }

        var sanitized = name is { Length: > 0 } ? SanitizeSessionName(name) : null;
        if (sanitized is not { Length: > 0 })
        {
            // Fallback: use the first few words of the user message.
            var words = firstMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            sanitized = string.Join(' ', words.Take(4));
            if (sanitized.Length > 60) sanitized = sanitized[..57] + "…";
        }

        if (sanitized.Length > 0)
        {
            Rename(sanitized);
            UpdateState(s => s with { SessionTitle = sanitized });
        }
    }

    private void FlushNewMessages()
    {
        if (_harness is null) return;
        var all = _harness.Messages;
        for (var i = _lastMessageCount; i < all.Count; i++)
            AppendMessage(all[i]);
        _lastMessageCount = all.Count;
    }

    private void MarkPersisted()
    {
        if (_runtimeStarted && !_state.IsPersisted)
            UpdateState(s => s with { IsPersisted = true });
    }

    private void UpdateState(Func<SessionState, SessionState> update)
    {
        var next = update(_state);
        _state = next;
        StateChanged?.Invoke(next);
    }

    private void UpdateQueueCount()
    {
        int steering, followUp;
        lock (_lock)
        {
            steering = _steeringQueue.Count;
            followUp = _followUpQueue.Count;
        }
        UpdateState(s => s with
        {
            SteeringCount = steering,
            FollowUpCount = followUp,
        });
    }

    private List<UserMessage> DrainQueueLocked(Queue<UserMessage> queue)
    {
        lock (_lock)
        {
            if (queue.Count == 0) return [];
            var copy = queue.ToList();
            queue.Clear();
            return copy;
        }
    }

    private void ThrowIfNoRuntime()
    {
        if (!_runtimeStarted)
            throw new InvalidOperationException(
                "Call StartRuntime before using session actions.");
    }

    private static string SanitizeSessionName(string raw)
    {
        var trimmed = raw.Trim().Trim('"', '\'', '.', '!', '?');
        return trimmed.Length > 60 ? trimmed[..57] + "…" : trimmed;
    }

    // ──────── IDisposable ────────

    private int _disposed;

    /// <summary>
    /// Cancels any in-flight run, briefly awaits its completion, releases
    /// the run's <see cref="CancellationTokenSource"/>, and disposes the
    /// provider (releasing its HTTP transport). The provider is released
    /// even when no run ever started. Idempotent and thread-safe; intended
    /// for the end of the session's lifetime (TUI exit, session switch via
    /// <see cref="NewSessionAsync"/> / <see cref="ResumeAsync"/>, fixture
    /// teardown).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var cts = _runCts;
        if (cts is not null)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* run already cleaned up */ }

            var task = _currentRunTask;
            if (task is not null && !task.IsCompleted)
            {
                try { task.Wait(TimeSpan.FromSeconds(2)); }
                catch (AggregateException) { /* expected cancellation */ }
                catch (Exception) { /* timeout or other — don't block shutdown */ }
            }

            try { cts.Dispose(); }
            catch (ObjectDisposedException) { /* already disposed by RunAgentCoreAsync */ }
        }

        // Release the provider's HTTP transport. NullProvider and fakes are
        // no-ops; real providers dispose their HttpClient here.
        _provider?.Dispose();

        // Tear down the extension runtime (ALC unload, hook/event dispatch
        // disposal) before this session becomes unreachable.
        _extensionRuntime?.Dispose();
    }
}
