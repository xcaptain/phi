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
/// <para>
/// The editor stays enabled while the agent is running: a submitted prompt
/// during a run enqueues into <see cref="MessageQueue"/> as a steering
/// message and surfaces as <c>+N queued</c> on the status bar. The harness
/// drains the queue at turn boundaries, so the next turn picks up the
/// redirect automatically. Esc cancels the in-flight turn — the harness
/// appends synthetic interrupted tool placeholders so the next prompt sees
/// a well-formed history.
/// </para>
/// </summary>
public sealed class PhiTuiApp(Harness harness, string model)
{
    private readonly MessageQueue _queue = new();
    private CancellationTokenSource? _runCts;

    public void Run()
    {
        using var session = Terminal.Open();

        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(model);
        var inputText = new State<string?>(string.Empty);

        // Bind queue counters to the status bar so the user always sees how
        // many messages are waiting to be picked up.
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
                if (command == "/exit") editor.App?.Stop();
                return;
            }

            // While the agent is running, queue the prompt as steering so it
            // lands on the next turn boundary instead of being silently dropped.
            if (status.Running.Value)
            {
                _queue.EnqueueSteering(new UserMessage { Content = text });
                transcript.AddUserMessage($"[queued · steering] {text}");
                return;
            }

            transcript.AddUserMessage(text);
            _ = RunAgentAsync(transcript, status, editor, text);
        });

        // Esc cancels the in-flight turn. Harness handles the cancel by
        // appending interrupted tool placeholders, so the next prompt sees
        // a coherent history and the session stays alive.
        editor.Canceled((_, _) => _runCts?.Cancel());

        Terminal.Run(root, () => TerminalLoopResult.Continue);
    }

    private System.Threading.Tasks.Task BindQueueCountToStatusBar(PhiStatusBar status)
    {
        // Lightweight polling binder: the MessageQueue doesn't expose events,
        // so we sample every 200ms. Cheap, and avoids complicating the queue
        // with INotifyPropertyChanged just for this single consumer.
        return System.Threading.Tasks.Task.Run(async () =>
        {
            while (true)
            {
                status.QueuedCount.Value = _queue.SteeringCount + _queue.FollowUpCount;
                try { await System.Threading.Tasks.Task.Delay(200); }
                catch (System.Threading.Tasks.TaskCanceledException) { return; }
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
        {
            return new PromptEditorCompletion(false, null, 0, 0);
        }

        var candidates = SlashCommands.Complete(prefix);
        if (candidates.Count == 0)
        {
            return new PromptEditorCompletion(false, null, 0, 0);
        }

        string? ghost = null;
        if (caret == text.Length && candidates[0].Length > prefix.Length)
        {
            ghost = candidates[0][prefix.Length..];
        }

        return new PromptEditorCompletion(true, candidates, 0, caret, 0, ghost);
    }

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
            }
        }
        catch (Exception ex)
        {
            transcript.AddError(ex.Message);
        }
        finally
        {
            status.Running.Value = false;
            _runCts.Dispose();
            _runCts = null;
            editor.App?.Focus(editor);
        }
    }
}
