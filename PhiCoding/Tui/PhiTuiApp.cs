using PhiAgent;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Tui;

/// <summary>
/// Thin TUI shell around <see cref="ISession"/>. Renders session
/// state via bound controls; user actions are forwarded to the session.
/// </summary>
public sealed class PhiTuiApp
{
    private readonly ISession _session;

    public PhiTuiApp(ISession session)
    {
        _session = session;
    }

    public (Visual Root, PromptEditor Editor) BuildRoot()
    {
        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(_session.State.Model);
        var inputText = new State<string?>(string.Empty);

        BindTranscriptToSession(transcript);
        BindStatusBarToEngine(status);

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

        var modelMarkup = new Markup($"[dim]{_session.State.Model}[/]") { Wrap = false };
        _session.StateChanged += _ => modelMarkup.Text = $"[dim]{_session.State.Model}[/]";

        var header = new Header
        {
            Left = new Markup("[bold]phi[/]") { Wrap = false },
            Right = modelMarkup,
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

    private void BindStatusBarToEngine(PhiStatusBar status)
    {
        _session.StateChanged += s =>
        {
            status.Running.Value = s.IsRunning;
            status.QueuedCount.Value = s.SteeringCount + s.FollowUpCount;
        };
        status.Running.Value = _session.State.IsRunning;
        status.QueuedCount.Value = _session.State.SteeringCount + _session.State.FollowUpCount;
    }

    // ──────── /sessions dialog ────────

    private void ShowSessionsDialog(ChatTranscript transcript, PromptEditor editor)
    {
        var sessions = _session.ListRecentSessions(7);
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

        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        foreach (var group in grouped)
        {
            var label = group.Key == today ? "Today"
                : group.Key == today.AddDays(-1) ? "Yesterday"
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
            SessionRecord? target = null;
            var idx = 0;
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
            transcript.ResetRenderedCount();
            _ = _session.ResumeSession(target.Id);
        });

        var dialog = new Dialog(new Markup("[bold]Sessions (last 7 days)[/]"), list)
        {
            IsResizable = false, IsDraggable = true, IsModal = true,
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

    // ──────── Slash completion ────────

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
