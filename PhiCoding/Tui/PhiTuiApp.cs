using System.Text;
using PhiAgent;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Tui;

public sealed class PhiTuiApp(Harness harness, string model, CodingSession session, IPhiProvider provider)
{
    private readonly MessageQueue _queue = new();
    private CancellationTokenSource? _runCts;
    private int _lastMessageCount;
    private bool _autoNamed;

    public void Run()
    {
        using var terminal = Terminal.Open();

        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(model);
        var inputText = new State<string?>(string.Empty);

        _ = BindQueueCountToStatusBar(status);

        var editor = new PromptEditor()
            .Prompt(new Markup("[primary]❯[/] "))
            .ContinuationPromptMarkup("[dim]·[/]")
            .Text(inputText)
            .Placeholder("Ask Phi anything… (Enter submit · Shift+Enter newline · Esc cancel · Ctrl+Q quit)")
            .CompletionPresentation(PromptEditorCompletionPresentation.PopupList)
            .CompletionHandler(CompleteSlashCommand)
            .MinHeight(3)
            .MaxHeight(10)
            .AutoFocus(true);

        var header = new Header
        {
            Left = new Markup("[bold]phi[/]") { Wrap = false },
            Right = new Markup($"[dim]{model}[/]") { Wrap = false },
        };

        var root = new DockLayout()
            .Top(header)
            .Content(transcript.Visual)
            .Bottom(new VStack(editor.Scrollable(), status.Visual).Spacing(0)
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
                    case "/sessions":
                        ShowSessionsDialog(transcript, editor);
                        break;
                    case "/exit":
                        editor.App?.Stop();
                        break;
                }
                return;
            }

            if (status.Running.Value)
            {
                _queue.EnqueueSteering(new UserMessage { Content = text });
                transcript.AddUserMessage($"[queued · steering] {text}");
                return;
            }

            AppendUserMessage(transcript, text);
            _ = RunAgentAsync(transcript, status, editor, text);
        });

        editor.Canceled((_, _) => _runCts?.Cancel());

        Terminal.Run(root, () => TerminalLoopResult.Continue);
    }

    // ──────────────────── Auto‑naming on first message ────────────────────

    private async Task TryAutoNameSessionAsync(string firstMessage)
    {
        if (_autoNamed) return;
        _autoNamed = true;

        try
        {
            var prompt = $"Create a concise session name in at most 4 words for this user message:\n\n{firstMessage}";
            var name = "";
            var msgs = new List<IAgentMessage> { new UserMessage { Content = prompt } };

            await foreach (var ev in provider.StreamResponseAsync(
                model, "You write concise session names.", msgs, [], default))
            {
                if (ev is ProviderTextDeltaEvent t) name += t.Delta;
            }

            var sanitized = SanitizeSessionName(name);
            if (sanitized is { Length: > 0 })
                session.Rename(sanitized);
        }
        catch
        {
            // Auto-naming must never disrupt the agent flow.
        }
    }

    private static string SanitizeSessionName(string raw)
    {
        var trimmed = raw.Trim().Trim('"', '\'', '.', '!', '?');
        return trimmed.Length > 60 ? trimmed[..57] + "…" : trimmed;
    }

    // ──────────────────── Session message tracking ────────────────────

    private void AppendUserMessage(ChatTranscript transcript, string text)
    {
        transcript.AddUserMessage(text);
        // Do NOT write to session here — FlushNewMessages in
        // RunAgentAsync will pick up the prompt from harness.Messages
        // after harness.RunAsync adds it. Writing here would duplicate
        // the entry when FlushNewMessages runs moments later.
        _ = TryAutoNameSessionAsync(text);
    }

    private void FlushNewMessages(ChatTranscript _)
    {
        var all = harness.Messages;
        for (var i = _lastMessageCount; i < all.Count; i++)
            session.AppendMessage(all[i]);
        _lastMessageCount = all.Count;
    }

    // ──────────────────── /sessions dialog ────────────────────

    private void ShowSessionsDialog(ChatTranscript transcript, PromptEditor editor)
    {
        var index = new SessionIndex(SessionPaths.IndexFileFor(Environment.CurrentDirectory));
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        var all = index.ListAll().Where(r => r.UpdatedAt >= cutoff).ToList();
        if (all.Count == 0)
        {
            transcript.AddError("No sessions in the last 7 days");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var grouped = all
            .GroupBy(r => DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt).DateTime))
            .OrderByDescending(g => g.Key)
            .ToList();

        var list = new OptionList<OptionListItem>().ActivateOnClick(true);

        foreach (var group in grouped)
        {
            var label = group.Key == today
                ? "Today"
                : group.Key == today.AddDays(-1)
                    ? "Yesterday"
                    : group.Key.ToString("MMM d");
            list.Items.Add(new OptionListItem(label) { IsEnabled = false });

            foreach (var r in group.OrderByDescending(x => x.UpdatedAt))
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt)
                    .ToLocalTime().ToString("HH:mm");
                var title = r.Title ?? r.Id[..8];
                list.Items.Add(new OptionListItem($"  {title} · {time} · {r.Model}"));
            }
        }

        list.ItemActivated((_, e) =>
        {
            // Map list index back to session record, skipping disabled headers.
            var idx = 0;
            SessionRecord? target = null;
            foreach (var g in grouped)
            {
                foreach (var r in g.OrderByDescending(x => x.UpdatedAt))
                {
                    idx++;
                    if (idx == e.Index) { target = r; break; }
                }
                if (target is not null) break;
            }

            if (target is null) return;

            if (list.Parent is Dialog d) d.Close();
            _ = SwitchToSessionAsync(target, transcript, editor);
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

    private async Task SwitchToSessionAsync(
        SessionRecord target, ChatTranscript transcript, PromptEditor editor)
    {
        // Flush current session.
        FlushNewMessages(transcript);

        // Load target.
        CodingSession newSession;
        try
        {
            newSession = CodingSession.Resume(target.Id, Environment.CurrentDirectory);
        }
        catch
        {
            transcript.AddError($"Failed to load session '{target.Id}'");
            return;
        }

        var loaded = newSession.LoadMessages();
        harness.ReplaceMessages(loaded);
        transcript.ClearAndLoad(loaded);

        session = newSession;
        _lastMessageCount = loaded.Count;
        _autoNamed = target.Title is { Length: > 0 };

        editor.App?.Focus(editor);
    }

    // ──────────────────── Agent loop ────────────────────

    private async Task RunAgentAsync(
        ChatTranscript transcript, PhiStatusBar status, PromptEditor editor, string prompt)
    {
        status.Running.Value = true;
        _runCts = new CancellationTokenSource();
        try
        {
            await foreach (var ev in harness.RunAsync(
                prompt,
                getSteeringMessages: () => _queue.DrainSteering(),
                getFollowUpMessages: () => _queue.DrainFollowUp(),
                cancellationToken: _runCts.Token))
            {
                status.Apply(ev);
                transcript.Apply(ev);
                FlushNewMessages(transcript);
            }
        }
        catch (Exception ex)
        {
            transcript.AddError(ex.Message);
        }
        finally
        {
            status.Running.Value = false;
            FlushNewMessages(transcript);
            _runCts.Dispose();
            _runCts = null;
            editor.App?.Focus(editor);
        }
    }

    private Task BindQueueCountToStatusBar(PhiStatusBar status)
    {
        return System.Threading.Tasks.Task.Run(async () =>
        {
            while (true)
            {
                status.QueuedCount.Value = _queue.SteeringCount + _queue.FollowUpCount;
                try { await Task.Delay(200); }
                catch (TaskCanceledException) { return; }
            }
        });
    }

    private static PromptEditorCompletion CompleteSlashCommand(in PromptEditorCompletionRequest request)
    {
        var snapshot = request.Snapshot;
        var caret = Math.Clamp(request.CaretIndex, 0, snapshot.Length);
        var text = string.Create(snapshot.Length, snapshot, static (span, s) => s.CopyTo(0, span));

        var prefix = text[..caret];
        if (prefix.Contains(' ') || prefix.Contains('\n'))
            return new PromptEditorCompletion(false, null, 0, 0);

        var candidates = SlashCommands.Complete(prefix);
        if (candidates.Count == 0)
            return new PromptEditorCompletion(false, null, 0, 0);

        string? ghost = null;
        if (caret == text.Length && candidates[0].Length > prefix.Length)
            ghost = candidates[0][prefix.Length..];

        return new PromptEditorCompletion(true, candidates, 0, caret, 0, ghost);
    }
}
