using System.Text;
using PhiAgent;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Tui;

/// <summary>
/// Composition root: fullscreen layout (header / transcript / prompt / status
/// bar) and the agent pump. Harness events are applied on the UI thread —
/// the dispatcher synchronization context installed by the terminal loop
/// brings <c>await foreach</c> continuations back onto it.
/// </summary>
public sealed class PhiTuiApp(Harness harness, string model, CodingSession session)
{
    private readonly MessageQueue _queue = new();
    private CancellationTokenSource? _runCts;
    private int _lastMessageCount;

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
                        _ = SummarizeAndExitAsync(transcript, editor);
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

    // ──────────────────── Session message tracking ────────────────────

    private void AppendUserMessage(ChatTranscript transcript, string text)
    {
        transcript.AddUserMessage(text);
        session.AppendMessage(new UserMessage { Content = text });
        _lastMessageCount = harness.Messages.Count;
    }

    private void FlushNewMessages(ChatTranscript _)
    {
        var all = harness.Messages;
        for (var i = _lastMessageCount; i < all.Count; i++)
        {
            session.AppendMessage(all[i]);
        }
        _lastMessageCount = all.Count;
    }

    // ──────────────────── /sessions dialog ────────────────────

    private void ShowSessionsDialog(ChatTranscript transcript, PromptEditor editor)
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(SessionPaths.DefaultRoot));
        var sessions = ListRecentSessions(index, days: 7);
        if (sessions.Count == 0)
        {
            transcript.AddError("No sessions in the last 7 days");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var grouped = sessions
            .GroupBy(r => DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt).DateTime))
            .OrderByDescending(g => g.Key)
            .ToList();

        // Build the dialog content.
        var content = new VStack().Spacing(1);
        foreach (var group in grouped)
        {
            var label = group.Key == today
                ? "Today"
                : group.Key == today.AddDays(-1)
                    ? "Yesterday"
                    : group.Key.ToString("MMM d");
            var items = new VStack().Spacing(0);
            foreach (var r in group)
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt)
                    .ToLocalTime().ToString("HH:mm");
                var title = r.Title ?? r.Id[..8];
                items.Add(new Markup(
                    $"  [primary]{ToolCardRenderer.Escape(title)}[/] [dim]{time} · {r.Model}[/]")
                { Wrap = false });
            }
            content.Add(new Group(
                new Markup($"[bold][dim]{label}[/][/]"), items)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start));
        }

        var dialog = new Dialog(
            new Markup("[bold]Sessions (last 7 days)[/]"),
            content)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.Show();
    }

    private static List<SessionRecord> ListRecentSessions(SessionIndex index, int days)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();
        return index.ListAll().Where(r => r.UpdatedAt >= cutoff).ToList();
    }

    // ──────────────────── /exit summary ────────────────────

    private async Task SummarizeAndExitAsync(ChatTranscript transcript, PromptEditor editor)
    {
        transcript.AddUserMessage("Summarizing session…");
        FlushNewMessages(transcript);

        var messages = harness.Messages;
        if (messages.Count < 2)
        {
            var first = messages.OfType<UserMessage>().FirstOrDefault();
            if (first is not null)
            {
                var t = first.Text.Length > 60 ? first.Text[..60] + "…" : first.Text;
                session.Rename(t);
            }
            editor.App?.Stop();
            return;
        }

        try
        {
            var summary = await SummarizeAsync();
            if (summary is { Length: > 0 }) session.Rename(summary);
        }
        catch
        {
            var first = messages.OfType<UserMessage>().FirstOrDefault();
            if (first is not null)
            {
                var t = first.Text.Length > 60 ? first.Text[..60] + "…" : first.Text;
                session.Rename(t);
            }
        }

        editor.App?.Stop();
    }

    private async Task<string?> SummarizeAsync()
    {
        // Build a slim summarisation prompt from the minimal provider
        // configuration. We reuse the same harness provider to avoid
        // constructing a new one.
        var providerField = typeof(Harness).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (providerField?.GetValue(harness) is not IPhiProvider provider)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("Summarise this coding session in one short sentence (max 60 chars).");
        sb.AppendLine();
        foreach (var msg in harness.Messages)
        {
            switch (msg)
            {
                case UserMessage u: sb.AppendLine($"User: {u.Text}"); break;
                case AssistantMessage a: sb.AppendLine($"Assistant: {a.Text[..Math.Min(a.Text.Length, 100)]}"); break;
                case ToolResultMessage t:
                    var status = t.IsError ? "error" : "ok";
                    sb.AppendLine($"Tool ({t.ToolName}): {status}");
                    break;
            }
        }

        var summary = "";
        var messages = new List<IAgentMessage> { new UserMessage { Content = sb.ToString() } };

        try
        {
            await foreach (var ev in provider.StreamResponseAsync(
                model, "You are a session summariser.", messages, [], default))
            {
                if (ev is ProviderTextDeltaEvent t) summary += t.Delta;
            }
        }
        catch
        {
            return null;
        }

        return summary.Trim();
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
