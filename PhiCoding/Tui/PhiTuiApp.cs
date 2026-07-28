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
public sealed class PhiTuiApp(Harness harness, string model)
{
    private CancellationTokenSource? _runCts;

    public void Run()
    {
        using var session = Terminal.Open();

        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(model);
        var inputText = new State<string?>(string.Empty);

        var editor = new PromptEditor()
            .Prompt(new Markup("[primary]❯[/] "))
            .ContinuationPromptMarkup("[dim]·[/]")
            .Text(inputText)
            .Placeholder("Ask Phi anything… (Enter submit · Shift+Enter newline · Esc cancel · Ctrl+Q quit)")
            .IsEnabled(() => !status.Running.Value)
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
            if (text.Length == 0 || status.Running.Value)
            {
                return;
            }

            if (SlashCommands.Match(text) is { } command)
            {
                switch (command)
                {
                    case "/exit":
                        editor.App?.Stop();
                        break;
                }

                return;
            }

            transcript.AddUserMessage(text);
            _ = RunAgentAsync(transcript, status, editor, text);
        });

        editor.Canceled((_, _) => _runCts?.Cancel());

        Terminal.Run(root, () => TerminalLoopResult.Continue);
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
            await foreach (var ev in harness.RunAsync(prompt, cancellationToken: _runCts.Token))
            {
                status.Apply(ev);
                transcript.Apply(ev);
            }
        }
        catch (OperationCanceledException)
        {
            transcript.AddError("cancelled");
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
