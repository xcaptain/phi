using System.Globalization;
using PhiAgent;
using PhiCoding.Providers;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Tui;

/// <summary>
/// Thin TUI shell around <see cref="ISession"/>. Renders session
/// state via bound controls; user actions are forwarded to the session.
/// <see cref="IDisposable.Dispose"/> cancels the in-flight run and
/// releases session resources; call when the TUI exits (Ctrl+Q, /exit).
/// </summary>
public sealed class PhiTuiApp(ISession session, ProviderManager? providers = null) : IDisposable
{
    private readonly ISession _session = session;
    private readonly ProviderManager _providers = providers ?? new ProviderManager();

    /// <summary>
    /// Chat transcript created by the latest <see cref="BuildRoot"/> call.
    /// Null until <see cref="BuildRoot"/> is invoked. Exposed so callers
    /// (and tests) can observe the rendered message list directly.
    /// </summary>
    public ChatTranscript? Transcript { get; private set; }

    /// <summary>
    /// Status bar created by the latest <see cref="BuildRoot"/> call.
    /// Null until <see cref="BuildRoot"/> is invoked.
    /// </summary>
    public PhiStatusBar? StatusBar { get; private set; }

    /// <summary>
    /// Suggestion strip created by the latest <see cref="BuildRoot"/> call.
    /// Null until <see cref="BuildRoot"/> is invoked.
    /// </summary>
    public SuggestionStrip? SuggestionStrip { get; private set; }

    // Last message already recorded in the transcript as a persistent error.
    // LastError stays set between StateChanged events until the next run
    // clears it, so the same message can re-arrive on consecutive state
    // changes; dedup keeps the transcript from duplicating the record while
    // still letting the status bar show the error.
    private string? _lastRoutedError;

    /// <summary>
    /// Disposes the wrapped <see cref="ISession"/>, cancelling and
    /// awaiting any active run. Idempotent and safe to call after the
    /// TUI has already torn down.
    /// </summary>
    public void Dispose()
    {
        _session.Dispose();
    }

    public (Visual Root, PromptEditor Editor) BuildRoot()
    {
        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(_session.State.Model);
        Transcript = transcript;
        StatusBar = status;
        var inputText = new State<string?>(string.Empty);

        BindTranscriptToSession(transcript);
        BindStatusBarToEngine(status, transcript);

        // Live autocomplete strip: shows filtered slash commands + descriptions
        // as you type; collapses when the input isn't a command token.
        var suggestionStrip = new SuggestionStrip(inputText, [new SlashCommandProvider()]);
        SuggestionStrip = suggestionStrip;

        var editor = new PromptEditor()
            .Prompt(new Markup("[primary]❯[/] "))
            .ContinuationPromptMarkup("[dim]·[/]")
            .Text(inputText)
            .Placeholder("Ask Phi anything… (Enter submit · Esc cancel · Ctrl+Q quit)")
            .CompletionPresentation(PromptEditorCompletionPresentation.PopupList)
            .CompletionHandler(CompleteSlashCommand)
            .MinHeight(3)
            .MaxHeight(10)
            .AutoFocus(true);

        var modelMarkup = new Markup($"[dim]{FormatModel(_session.State.ProviderName, _session.State.Model)}[/]") { Wrap = false };
        _session.StateChanged += _ =>
            modelMarkup.Text = $"[dim]{FormatModel(_session.State.ProviderName, _session.State.Model)}[/]";

        var header = new Header
        {
            Left = new Markup("[bold]phi[/]") { Wrap = false },
            Right = modelMarkup,
        };

        var root = new DockLayout()
            .Top(header)
            .Content(transcript.Visual)
            .Bottom(new VStack(editor.Scrollable(), suggestionStrip.Visual, status.Visual).Spacing(0)
                .Margin(new Thickness(0, 1, 0, 0)))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);

        editor.Accepted((_, e) =>
        {
            var text = e.Text.Trim();
            inputText.Value = string.Empty;
            if (text.Length == 0) return;

            if (SlashCommands.Match(text) is { } command)
            {
                switch (command)
                {
                    case "/new":
                        _ = NewSessionAsync(transcript);
                        break;
                    case "/sessions":
                        ShowSessionsDialog(transcript, editor);
                        break;
                    case "/connect":
                        ShowConnectDialog(transcript, editor);
                        break;
                    case "/models":
                        ShowModelsDialog(transcript, editor);
                        break;
                    case "/exit":
                        editor.App?.Stop();
                        break;
                }
                return;
            }

            if (SlashCommands.MatchWithArgs(text) is { } withArgs)
            {
                switch (withArgs.Command)
                {
                    case "/connect":
                        ConnectProviderByName(withArgs.Args, transcript, editor);
                        break;
                    case "/models":
                        SwitchModelByName(withArgs.Args, transcript);
                        break;
                }
                return;
            }

            if (_session.State.IsRunning)
            {
                _session.EnqueueSteering(new UserMessage { Content = text });
                transcript.AddUserMessage($"[queued · steering] {text}");
                return;
            }

            transcript.AddUserMessage(text);
            _session.SubmitPrompt(text);
        });

        editor.Canceled((_, _) => _session.Cancel());

        return (root, editor);
    }

    public void Run()
    {
        using var terminal = Terminal.Open();
        var (root, _) = BuildRoot();
        Terminal.Run(root, () => TerminalLoopResult.Continue);
    }

    // ──────── Engine bindings ────────

    private void BindTranscriptToSession(ChatTranscript transcript)
    {
        transcript.Bind(_session);
    }

    private void BindStatusBarToEngine(PhiStatusBar status, ChatTranscript transcript)
    {
        _session.StateChanged += s =>
        {
            status.Running.Value = s.IsRunning;
            status.QueuedCount.Value = s.SteeringCount + s.FollowUpCount;
            status.UpdateStats(s.Stats);
            status.UpdateContext(s.ContextUsedTokens, s.AutoCompactThreshold);
            status.UpdateModel(s.ProviderName, s.Model);

            // Event-driven error clear: any state change without a new
            // LastError wipes the previous error from the status bar.
            // A non-empty LastError replaces whatever is currently shown.
            if (s.LastError is { Length: > 0 } err)
                RouteError(status, transcript, err);
            else
            {
                // Clean state (e.g. a new run started and cleared LastError):
                // restore the status bar and reset dedup so a *new*
                // occurrence of the same error message gets a fresh
                // transcript record.
                status.ClearError();
                _lastRoutedError = null;
            }
        };

        _session.HarnessEvent += e =>
        {
            if (e is HarnessErrorEvent he)
                RouteError(status, transcript, he.Message);
        };

        status.Running.Value = _session.State.IsRunning;
        status.QueuedCount.Value = _session.State.SteeringCount + _session.State.FollowUpCount;
        status.UpdateStats(_session.State.Stats);
        status.UpdateContext(_session.State.ContextUsedTokens, _session.State.AutoCompactThreshold);
        status.UpdateModel(_session.State.ProviderName, _session.State.Model);
        if (_session.State.LastError is { Length: > 0 } initial)
            RouteError(status, transcript, initial);
    }

    /// <summary>
    /// Classifies an error and routes it: every error goes to the status bar,
    /// persistent errors additionally leave a transcript line so the user
    /// can scroll back to them after the status bar clears.
    /// The same message re-arriving on a later state change (LastError stays
    /// set until the next run clears it) is deduplicated — it updates the
    /// status bar but does not append a second transcript record.
    /// </summary>
    private void RouteError(PhiStatusBar status, ChatTranscript transcript, string message)
    {
        var isTransient = ErrorClassifier.LooksTransient(message);
        status.ShowError(message, isPersistent: !isTransient);
        if (isTransient) return;
        if (_lastRoutedError == message) return;
        _lastRoutedError = message;
        transcript.AddPersistentError(message);
    }

    // ──────── /new ────────

    /// <summary>
    /// Starts a fresh session: the session swaps itself in place to a new
    /// empty record and the transcript is rebuilt empty. The status bar
    /// keeps the current provider/model (the user is already connected).
    /// </summary>
    private async Task NewSessionAsync(ChatTranscript transcript)
    {
        transcript.ClearAndLoad([]);
        await _session.NewSession();
        _lastRoutedError = null;
        transcript.AddInfo("New session started");
    }

    // ──────── /sessions dialog ────────

    private void ShowSessionsDialog(ChatTranscript transcript, PromptEditor editor)
    {
        var sessions = _session.ListRecentSessions(7);
        if (sessions.Count == 0)
        {
            transcript.AddInfo("No sessions in the last 7 days");
            return;
        }

        var (list, recordsByIndex) = BuildSessionPicker(sessions);

        list.ItemActivated((_, e) =>
        {
            var target = (uint)e.Index < (uint)recordsByIndex.Count
                ? recordsByIndex[e.Index]
                : null;
            if (target is null) return;

            if (list.Parent is Dialog d) d.Close();
            transcript.ResetRenderedCount();
            _ = _session.ResumeSession(target.Id);
        });

        var dialog = new Dialog(new Markup("[bold]Sessions (last 7 days)[/]"), list)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                editor.App?.Focus(editor);
            }
        };
        dialog.Show();
    }

    /// <summary>
    /// Builds the /sessions picker items and a position-parallel record map.
    /// <c>OptionList</c>'s <c>ItemActivated</c> index is the raw item position
    /// (date-group headers included), so activation must look the record up by
    /// position — a record-counting loop drifts once sessions span multiple
    /// day groups.
    /// </summary>
    internal static (OptionList<OptionListItem> List, List<SessionRecord?> Records)
        BuildSessionPicker(IReadOnlyList<SessionRecord> sessions)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var grouped = sessions
            .GroupBy(r => DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt).DateTime))
            .OrderByDescending(g => g.Key)
            .ToList();

        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        var recordsByIndex = new List<SessionRecord?>();
        foreach (var group in grouped)
        {
            var label = group.Key == today ? "Today"
                : group.Key == today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMM d", CultureInfo.InvariantCulture);
            list.Items.Add(new OptionListItem(label) { IsEnabled = false });
            recordsByIndex.Add(null);

            foreach (var r in group.OrderByDescending(x => x.UpdatedAt))
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt)
                    .ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
                var title = r.Title ?? r.Id[..8];
                var model = r.ProviderName.Length > 0
                    ? $"{r.ProviderName}/{r.Model}"
                    : r.Model;
                list.Items.Add(new OptionListItem($"  {title} · {time} · {model}"));
                recordsByIndex.Add(r);
            }
        }
        return (list, recordsByIndex);
    }

    // ──────── /connect dialog ────────

    private void ShowConnectDialog(ChatTranscript transcript, PromptEditor editor)
    {
        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        var currentProvider = _session.State.ProviderName;
        foreach (var entry in _providers.Providers)
        {
            var connected = entry.Name.Equals(currentProvider, StringComparison.OrdinalIgnoreCase);
            var noKey = !_providers.HasApiKey(entry);
            var label = $"  {(connected ? "✓ " : "  ")}{entry.DisplayName} — {entry.Name}"
                + (noKey ? "  (no key)" : "");
            list.Items.Add(new OptionListItem(label));
        }

        list.ItemActivated((_, e) =>
        {
            var entry = _providers.Providers[e.Index];
            if (list.Parent is Dialog d) d.Close();
            ConnectProvider(entry, transcript, editor);
        });

        var dialog = new Dialog(new Markup("[bold]Connect a provider[/]"), list)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                editor.App?.Focus(editor);
            }
        };
        dialog.Show();
    }

    internal void ConnectProviderByName(string name, ChatTranscript transcript, PromptEditor editor)
    {
        var entry = _providers.Providers.FirstOrDefault(
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            transcript.AddInfo($"Unknown provider '{name}'. Run /connect to pick one.");
            return;
        }
        ConnectProvider(entry, transcript, editor);
    }

    internal void ConnectProvider(ProviderCatalogEntry entry, ChatTranscript transcript, PromptEditor editor)
    {
        // Always prompt so the user can change an existing key (or keep it by
        // confirming the pre-filled value) — /connect must be able to modify
        // a stored/derived API key, not just set it the first time.
        var existingKey = _providers.ResolveApiKey(entry);
        PromptApiKey(entry, transcript, editor, existingKey,
            enteredKey => ApplyApiKeyAndConnect(entry, enteredKey, transcript));
    }

    /// <summary>
    /// Persists the entered key to the credential store and connects with it.
    /// </summary>
    internal void ApplyApiKeyAndConnect(
        ProviderCatalogEntry entry, string apiKey, ChatTranscript transcript)
    {
        _providers.SetApiKey(entry, apiKey);
        ConnectWithKey(entry, apiKey, transcript);
    }

    internal void ConnectWithKey(ProviderCatalogEntry entry, string apiKey, ChatTranscript transcript)
    {
        var model = _providers.ResolveDefaultModel(entry);
        var provider = _providers.CreateProvider(entry, apiKey);
        _session.SwitchProvider(provider, entry.Name, model);
        _providers.SaveDefault(entry, model);
        transcript.AddInfo($"Connected to {entry.Name} · {model}");
    }

    private static void PromptApiKey(
        ProviderCatalogEntry entry,
        ChatTranscript transcript,
        PromptEditor editor,
        string? existingKey,
        Action<string> onKey)
    {
        var textBox = new TextBox { IsPassword = true, Text = existingKey };
        var hint = existingKey is { Length: > 0 }
            ? $"[dim]API key for {entry.DisplayName} ({entry.Name}) — current key pre-filled; edit to replace, Enter to confirm[/]"
            : $"[dim]API key for {entry.DisplayName} ({entry.Name}) — Enter to confirm, Esc to cancel[/]";
        var body = new VStack(
            new Markup(hint),
            textBox).Spacing(1);

        var dialog = new Dialog(new Markup($"[bold]Connect {entry.DisplayName}[/]"), body)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Enter)
            {
                var value = (textBox.Text ?? "").Trim();
                if (value.Length == 0) return;
                dialog.Close();
                editor.App?.Focus(editor);
                onKey(value);
            }
            else if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                editor.App?.Focus(editor);
            }
        };
        dialog.Show();
        editor.App?.Focus(textBox);
    }

    // ──────── /models dialog ────────

    internal ProviderCatalogEntry? GetCurrentProviderEntry() =>
        _providers.Providers.FirstOrDefault(
            p => p.Name.Equals(_session.State.ProviderName, StringComparison.OrdinalIgnoreCase));

    private void ShowModelsDialog(ChatTranscript transcript, PromptEditor editor)
    {
        var entry = GetCurrentProviderEntry();
        if (entry is null)
        {
            transcript.AddInfo("No provider connected. Run /connect first.");
            return;
        }

        var currentModel = _session.State.Model;
        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        foreach (var model in entry.Models)
        {
            list.Items.Add(new OptionListItem($"  {(model == currentModel ? "✓ " : "  ")}{model}"));
        }

        list.ItemActivated((_, e) =>
        {
            var model = entry.Models[e.Index];
            if (list.Parent is Dialog d) d.Close();
            _session.SwitchModel(model);
            _providers.SaveDefault(entry, model);
            transcript.AddInfo($"Model: {model}");
        });

        var dialog = new Dialog(new Markup($"[bold]Models · {entry.Name}[/]"), list)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                editor.App?.Focus(editor);
            }
        };
        dialog.Show();
    }

    internal void SwitchModelByName(string model, ChatTranscript transcript)
    {
        var entry = GetCurrentProviderEntry();
        if (entry is null)
        {
            transcript.AddInfo("No provider connected. Run /connect first.");
            return;
        }
        if (!entry.Models.Contains(model))
        {
            transcript.AddInfo(
                $"Unknown model '{model}' for {entry.Name}. Available: {string.Join(", ", entry.Models)}");
            return;
        }

        _session.SwitchModel(model);
        _providers.SaveDefault(entry, model);
        transcript.AddInfo($"Model: {model}");
    }

    private static string FormatModel(string providerName, string model) =>
        providerName.Length > 0 ? $"{providerName}/{model}" : model;

    // ──────── Slash completion ────────

    private static readonly SlashCommandProvider SlashProvider = new();

    private static PromptEditorCompletion CompleteSlashCommand(in PromptEditorCompletionRequest request)
    {
        var snapshot = request.Snapshot;
        var caret = Math.Clamp(request.CaretIndex, 0, snapshot.Length);
        var text = string.Create(snapshot.Length, snapshot, static (span, s) => s.CopyTo(0, span));

        // Same tokenizer/filter as the suggestion strip, so Tab completion and
        // the live strip always agree.
        var match = SlashProvider.GetSuggestion(text, caret);
        if (match is null)
            return new PromptEditorCompletion(false, null, 0, 0);

        List<string> candidates = [.. match.Items.Select(i => i.Replacement)];
        var prefixLength = caret - match.ReplaceStart;

        string? ghost = null;
        if (caret == text.Length && candidates[0].Length > prefixLength)
            ghost = candidates[0][prefixLength..];

        return new PromptEditorCompletion(true, candidates, match.ReplaceStart, prefixLength, 0, ghost);
    }
}
