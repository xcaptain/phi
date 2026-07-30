using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Application-level session: the runtime environment for one
/// <see cref="Harness"/>. Owns the transcript (JSONL via
/// <see cref="SessionStorage"/>), the message queue, and the agent run
/// loop; publishes immutable <see cref="SessionState"/> snapshots via
/// <see cref="StateChanged"/> so frontends can react. Implements
/// <see cref="ISession"/> for UI binding.
/// <para>
/// Index bookkeeping is delegated to <see cref="SessionManager"/>.
/// Persistence is lazy: a fresh session holds an allocated id but writes
/// nothing until the first message (or explicit rename/touch) — see
/// <see cref="IsPersisted"/>.
/// </para>
/// </summary>
public sealed class CodingSession : ISession
{
    private readonly SessionManager _manager;
    private readonly object _lock = new();

    // Mutable: resume adopts the target session's storage and record.
    private SessionStorage _storage;
    private bool _persisted;

    private CodingSession(
        SessionRecord record, SessionStorage storage,
        SessionManager manager, bool persisted)
    {
        Record = record;
        _storage = storage;
        _manager = manager;
        _persisted = persisted;
    }

    public string Id => Record.Id;
    public string Cwd => _manager.Cwd;
    public string Model => Record.Model;
    public SessionRecord Record { get; private set; }
    public SessionStorage Storage => _storage;

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
    public static CodingSession GetOrCreateDefault(string cwd, string model)
    {
        var manager = new SessionManager(cwd);
        var record = manager.GetOrCreateDefaultSession(model);
        return new(record, OpenStorage(manager, record.Id), manager, persisted: true);
    }

    /// <summary>
    /// Creates a fresh session without writing anything to disk. The id is
    /// allocated eagerly; the transcript file and index record appear on
    /// the first persisted message.
    /// </summary>
    public static CodingSession Create(string cwd, string model, string? title = null)
    {
        var manager = new SessionManager(cwd);
        var record = manager.PrepareSession(model, title);
        return new(record, OpenStorage(manager, record.Id), manager, persisted: false);
    }

    /// <summary>
    /// Creates a fresh, fully initialized session from
    /// <see cref="SessionConfig"/>: builds the harness around the injected
    /// provider and calls <see cref="StartRuntime"/>. Persistence stays
    /// lazy — nothing hits disk until the first message.
    /// </summary>
    public static CodingSession Create(SessionConfig config)
    {
        var session = Create(config.Cwd, config.Model);
        session.StartRuntime(BuildHarness(config), config.Provider, config.Model);
        return session;
    }

    /// <summary>
    /// Opens an already-indexed session (persistence only — call
    /// <see cref="StartRuntime"/> before submitting prompts). Throws when
    /// the id is unknown.
    /// </summary>
    public static CodingSession Resume(string id, string cwd)
    {
        var manager = new SessionManager(cwd);
        var record = manager.GetSession(id);
        return new(record, OpenStorage(manager, id), manager, persisted: true);
    }

    /// <summary>
    /// Opens an already-indexed session with a full runtime: the stored
    /// transcript is loaded into the harness so the conversation can
    /// continue where it left off.
    /// </summary>
    public static CodingSession Resume(SessionConfig config, string id)
    {
        var session = Resume(id, config.Cwd);
        var harness = BuildHarness(config);
        harness.ReplaceMessages(session.LoadMessages());
        session.StartRuntime(harness, config.Provider, config.Model);
        return session;
    }

    private static Harness BuildHarness(SessionConfig config) =>
        new(
            config.Provider,
            config.Tools ?? BuiltInTools.CreateDefault(),
            model: config.Model,
            system: config.SystemPrompt,
            maxTurns: config.MaxTurns);

    private static SessionStorage OpenStorage(SessionManager manager, string id) =>
        new(manager.SessionFileFor(id));

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

    public IReadOnlyList<IAgentMessage> LoadMessages()
    {
        lock (_lock)
            return _storage.ReadAll()
                .Select(SessionEntryConverter.ToAgentMessage)
                .ToList();
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

    /// <summary>Indexed sessions of this project, newest first.</summary>
    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) =>
        _manager.ListSessions(days);

    // ──────── ISession explicit interface bridge ────────

    void ISession.RenameSession(string? title) => Rename(title);

    Task ISession.ResumeSession(string sessionId)
        => ResumeSessionById(sessionId);

    IReadOnlyList<SessionRecord> ISession.ListRecentSessions(int days)
        => ListRecentSessions(days);

    // ──────── Runtime (reactive engine state) ────────

    private Harness? _harness;
    private IPhiProvider? _provider;
    private MessageQueue? _queue;
    private string _runtimeModel = "";
    private CancellationTokenSource? _runCts;
    private Task? _currentRunTask;
    private int _lastMessageCount;
    private bool _autoNamed;
    private SessionState _state = SessionState.Empty;
    private bool _runtimeStarted;

    /// <summary>Fired on every <see cref="State"/> change.</summary>
    public event Action<SessionState>? StateChanged;

    /// <summary>Fired for streaming harness events (text deltas, tool calls).</summary>
    public event Action<HarnessEvent>? HarnessEvent;

    /// <summary>Latest immutable state.</summary>
    public SessionState State => _state;

    /// <summary>
    /// Starts the runtime: binds harness, provider, and model to this
    /// session. Must be called once before any action methods.
    /// </summary>
    public void StartRuntime(Harness harness, IPhiProvider provider, string model)
    {
        _harness = harness;
        _provider = provider;
        _runtimeModel = model;
        _queue = new MessageQueue();
        _lastMessageCount = harness.Messages.Count;
        _runtimeStarted = true;

        _state = new SessionState
        {
            Messages = harness.Messages.ToList(),
            SessionId = Id,
            Model = model,
            SessionTitle = Record.Title,
            IsPersisted = _persisted,
        };
    }

    // ──────── Runtime actions ────────

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

    public async Task ResumeSessionById(string sessionId)
    {
        ThrowIfNoRuntime();

        // 如果当前有 run 正在进行，先 cancel 并等它完全退出，
        // 避免 event loop 与 ResumeSessionCore 的 storage/harness 替换产生冲突。
        // Awaiting a completed task is a no-op, so no IsRunning check is
        // needed — and the task reference is cleared only after the run
        // has fully settled (see the finally block in RunAgentCoreAsync).
        var run = _currentRunTask;
        if (run is not null)
        {
            _runCts?.Cancel();
            await run;
        }

        ResumeSessionCore(sessionId);
    }

    public void EnqueueSteering(UserMessage message)
    {
        ThrowIfNoRuntime();
        _queue!.EnqueueSteering(message);
        UpdateQueueCount();
    }

    public void EnqueueFollowUp(UserMessage message)
    {
        ThrowIfNoRuntime();
        _queue!.EnqueueFollowUp(message);
        UpdateQueueCount();
    }

    // ──────── Internal engine loop ────────

    private async Task RunAgentCoreAsync(string prompt)
    {
        UpdateState(s => s with { IsRunning = true });

        // Auto-name on the first message only (_autoNamed guards
        // subsequent runs). Fire-and-forget — if the LLM call fails the
        // fallback still produces a title from the prompt text.
        _ = TryAutoNameSessionAsync(prompt);

        try
        {
            _runCts = new CancellationTokenSource();
            var harness = _harness!;

            await foreach (var ev in harness.RunAsync(
                prompt,
                getSteeringMessages: () => _queue!.DrainSteering(),
                getFollowUpMessages: () => _queue!.DrainFollowUp(),
                cancellationToken: _runCts.Token))
            {
                HarnessEvent?.Invoke(ev);

                // Persist after every event: each completed message lands
                // on disk immediately, so a crash mid-run loses at most
                // the in-flight message.
                FlushNewMessages();

                if (ev is TurnEndEvent te)
                {
                    UpdateState(s => s with
                    {
                        Messages = harness.Messages.ToList(),
                        Turn = s.Turn + 1,
                        Usage = te.FinalMessage.Usage,
                    });
                }
            }
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

    /// <summary>
    /// Adopts another session in place: loads its transcript and swaps
    /// record + storage, keeping this object's identity (frontends hold a
    /// single <see cref="ISession"/> reference across resumes).
    /// </summary>
    private void ResumeSessionCore(string sessionId)
    {
        FlushNewMessages();

        var record = _manager.FindSession(sessionId);
        if (record is null)
        {
            UpdateState(s => s with
            {
                LastError = $"Failed to load session '{sessionId}'",
            });
            return;
        }

        IReadOnlyList<IAgentMessage> loaded;
        lock (_lock)
        {
            _storage = OpenStorage(_manager, record.Id);
            Record = record;
            _persisted = true;
            loaded = _storage.ReadAll()
                .Select(SessionEntryConverter.ToAgentMessage)
                .ToList();
        }

        _harness!.ReplaceMessages(loaded);
        _lastMessageCount = loaded.Count;
        _autoNamed = record.Title is { Length: > 0 };

        UpdateState(s => new SessionState
        {
            Messages = loaded,
            SessionId = record.Id,
            Model = _runtimeModel,
            SessionTitle = record.Title,
            IsPersisted = true,
        });
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
        UpdateState(s => s with
        {
            SteeringCount = _queue?.SteeringCount ?? 0,
            FollowUpCount = _queue?.FollowUpCount ?? 0,
        });
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
}
