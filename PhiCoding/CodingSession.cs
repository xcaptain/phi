using System.Text;
using PhiAgent;
using PhiProvider;

namespace PhiCoding;

/// <summary>
/// Application-level session: owns persistence (JSONL), the harness (agent
/// dispatch), the provider (LLM), the message queue, and the agent loop.
/// Published immutable <see cref="SessionState"/> snapshots via
/// <see cref="StateChanged"/> so frontend layers can react.
/// Implements <see cref="ISession"/> for UI binding.
/// </summary>
public sealed class CodingSession : ISession
{
    // ──────── Persistence (existing) ────────

    private readonly SessionIndex _index;
    private readonly object _lock = new();
    private string _cwd;

    private CodingSession(SessionRecord record, SessionStorage storage, SessionIndex index, string cwd)
    {
        Record = record;
        Storage = storage;
        _index = index;
        _cwd = cwd;
    }

    public string Id => Record.Id;
    public string Cwd => _cwd;
    public string Model => Record.Model;
    public SessionRecord Record { get; private set; }
    public SessionStorage Storage { get; }

    public static CodingSession GetOrCreateDefault(string cwd, string model)
    {
        SessionPaths.EnsureRootFor(cwd);
        var index = new SessionIndex(SessionPaths.IndexFileFor(cwd));
        var id = SessionPaths.DefaultSessionId(cwd);
        var existing = index.Get(id);
        if (existing is not null) return new(existing, OpenStorage(cwd, id), index, cwd);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(id, cwd, model, "Default session", now, now);
        index.Upsert(record);
        return new(record, OpenStorage(cwd, id), index, cwd);
    }

    public static CodingSession Create(string cwd, string model, string? title = null)
    {
        SessionPaths.EnsureRootFor(cwd);
        var index = new SessionIndex(SessionPaths.IndexFileFor(cwd));
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(id, cwd, model, title, now, now);
        index.Upsert(record);
        return new(record, OpenStorage(cwd, id), index, cwd);
    }

    /// <summary>
    /// Creates a fully initialized session from <see cref="SessionConfig"/>.
    /// Builds the provider, tools, and harness internally; calls
    /// <see cref="StartRuntime"/> automatically.
    /// </summary>
    public static CodingSession Create(SessionConfig config)
    {
        var httpClient = new HttpClient();

        var provider = config.ProviderType.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicProvider(
                new AnthropicConfig
                {
                    ApiKey = config.ApiKey,
                    BaseUrl = config.BaseUrl,
                    Provider = config.ProviderName ?? config.ProviderType,
                },
                httpClient) as IPhiProvider,

            _ => new OpenAICompatibleProvider(
                new OpenAICompatibleConfig
                {
                    ApiKey = config.ApiKey,
                    BaseUrl = config.BaseUrl,
                    Provider = config.ProviderName ?? config.ProviderType,
                },
                httpClient) as IPhiProvider,
        };

        var tools = BuiltInTools.CreateDefault();
        var harness = new Harness(
            provider,
            tools,
            model: config.Model,
            system: config.SystemPrompt,
            maxTurns: config.MaxTurns);

        var session = Create(config.Cwd, config.Model);
        session.StartRuntime(harness, provider, config.Model);
        return session;
    }

    public static CodingSession Resume(string id, string cwd)
    {
        SessionPaths.EnsureRootFor(cwd);
        var indexPath = SessionPaths.IndexFileFor(cwd);
        var index = new SessionIndex(indexPath);
        var record = index.Get(id) ?? throw new InvalidOperationException(
            $"Session '{id}' not found for project '{cwd}'");
        return new(record, OpenStorage(cwd, id), index, cwd);
    }

    public void AppendMessage(IAgentMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = SessionEntryConverter.FromAgentMessage(message);
        lock (_lock) Storage.Append(entry);
        Touch();
    }

    public IReadOnlyList<IAgentMessage> LoadMessages()
    {
        lock (_lock) return Storage.ReadAll().Select(SessionEntryConverter.ToAgentMessage).ToList();
    }

    public void Touch()
    {
        var touched = Record with { UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        _index.Upsert(touched);
        Record = touched;
    }

    public void Rename(string? newTitle)
    {
        var renamed = Record with { Title = newTitle };
        _index.Upsert(renamed);
        Record = renamed;
    }

    // ──────── ISession explicit interface bridge ────────

    void ISession.RenameSession(string? title) => Rename(title);

    Task ISession.ResumeSession(string sessionId)
        => ResumeSessionById(sessionId);

    IReadOnlyList<SessionRecord> ISession.ListRecentSessions(int days)
        => ListRecentSessions(days);

    public static IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7)
    {
        var index = new SessionIndex(SessionPaths.IndexFileFor(Environment.CurrentDirectory));
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();
        return index.ListAll().Where(r => r.UpdatedAt >= cutoff).ToList();
    }

    private static SessionStorage OpenStorage(string cwd, string id) =>
        new(SessionPaths.SessionFileFor(cwd, id));

    // ──────── Runtime (reactive engine state) ────────

    private Harness? _harness;
    private IPhiProvider? _provider;
    private MessageQueue? _queue;
    private string _runtimeModel = "";
    private CancellationTokenSource? _runCts;
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
        };
    }

    // ──────── Runtime actions ────────

    public void SubmitPrompt(string text)
    {
        ThrowIfNoRuntime();
        if (_state.IsRunning) return;
        _ = RunAgentCoreAsync(text);
    }

    public void Cancel()
    {
        ThrowIfNoRuntime();
        _runCts?.Cancel();
    }

    public Task ResumeSessionById(string sessionId)
    {
        ThrowIfNoRuntime();
        return ResumeSessionAsync(sessionId, Environment.CurrentDirectory);
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

            FlushNewMessages();
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
        }
    }

    private async Task ResumeSessionAsync(string sessionId, string cwd)
    {
        FlushNewMessages();
        CodingSession target;
        try
        {
            target = CodingSession.Resume(sessionId, cwd);
        }
        catch
        {
            UpdateState(s => s with { LastError = $"Failed to load session '{sessionId}'" });
            return;
        }

        var loaded = target.LoadMessages();
        _harness!.ReplaceMessages(loaded);
        _lastMessageCount = loaded.Count;
        _autoNamed = target.Record.Title is { Length: > 0 };

        // Update this instance's record to match the loaded session.
        Record = target.Record;
        _cwd = target.Cwd;

        UpdateState(s => new SessionState
        {
            Messages = loaded,
            SessionId = target.Id,
            Model = _runtimeModel,
            SessionTitle = target.Record.Title,
        });
    }

    private async Task TryAutoNameSessionAsync(string firstMessage)
    {
        if (_autoNamed) return;
        _autoNamed = true;

        try
        {
            var prompt = $"Create a concise session name in at most 4 words for this user message:\n\n{firstMessage}";
            var name = "";
            var msgs = new List<IAgentMessage> { new UserMessage { Content = prompt } };

            await foreach (var ev in _provider!.StreamResponseAsync(
                _runtimeModel, "You write concise session names.", msgs, [], default))
            {
                if (ev is ProviderTextDeltaEvent t) name += t.Delta;
            }

            var sanitized = SanitizeSessionName(name);
            if (sanitized is { Length: > 0 })
            {
                Rename(sanitized);
                UpdateState(s => s with { SessionTitle = sanitized });
            }
        }
        catch { }
    }

    private void FlushNewMessages()
    {
        if (_harness is null) return;
        var all = _harness.Messages;
        for (var i = _lastMessageCount; i < all.Count; i++)
            AppendMessage(all[i]);
        _lastMessageCount = all.Count;
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
