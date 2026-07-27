using System.Collections.Concurrent;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using PhiAgent;
using UITextBlock = XenoAtom.Terminal.UI.Controls.TextBlock;

namespace PhiCoding.Tui;

/// <summary>
/// Phi TUI: single-session, multi-turn terminal interface for the Phi harness.
/// Layout:
///
///   ┌─ status bar ─────────────────┐
///   ├─ history (fills) ────────────┤
///   ├─ input prompt ───────────────┘
///
/// Streaming events from the agent task are pushed into <see cref="_pendingEvents"/>
/// and drained on each UI tick; user input is pushed into <see cref="_pendingSteering"/>
/// when the agent is busy (tau-style steering) or starts a new turn when idle.
/// </summary>
public sealed class PhiTuiApp
{
    private readonly Harness _harness;

    private readonly State<string> _history = new("");
    private readonly State<string> _status = new("ready");
    private readonly State<bool> _exit = new(false);

    private readonly ConcurrentQueue<HarnessEvent> _pendingEvents = new();
    private readonly ConcurrentQueue<UserMessage> _pendingSteering = new();

    private Task? _runTask;

    public PhiTuiApp(Harness harness)
    {
        _harness = harness;
    }

    public void Run()
    {
        var input = new State<string>("");

        var historyBlock = new UITextBlock(() => _history.Value)
        {
            Wrap = true,
        };

        var statusBlock = new UITextBlock(() => $"[{_status.Value}]");

        var inputEditor = new PromptEditor(input);
        inputEditor.AcceptedRouted += (_, args) => OnSubmitted(args.Text, input);

        var exitButton = new Button("Exit").Click(() => _exit.Value = true);

        var statusBar = new HStack(statusBlock, exitButton);

        var layout = new DockLayout(
            top: statusBar,
            content: historyBlock,
            bottom: inputEditor);

        Terminal.Run(
            layout,
            onUpdate: () =>
            {
                DrainEvents();
                CheckRunCompletion();
                return _exit.Value
                    ? TerminalLoopResult.StopAndKeepVisual
                    : TerminalLoopResult.Continue;
            });
    }

    private void OnSubmitted(string text, State<string> inputState)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _history.Value += $"\n> {text}\n";

        if (_runTask is not null)
        {
            _pendingSteering.Enqueue(new UserMessage { Content = text });
            _history.Value += "[queued — will steer next turn]\n";
        }
        else
        {
            _status.Value = "running";
            _runTask = Task.Run(() => RunAgentAsync(text));
        }
        inputState.Value = "";
    }

    private void DrainEvents()
    {
        while (_pendingEvents.TryDequeue(out var ev))
        {
            _history.Value += EventFormatter.Format(ev);
        }
    }

    private void CheckRunCompletion()
    {
        if (_runTask is { IsCompleted: true })
        {
            _runTask = null;
            _status.Value = "ready";
        }
    }

    private async Task RunAgentAsync(string prompt)
    {
        Func<IReadOnlyList<IAgentMessage>> getSteering = () =>
        {
            var list = new List<IAgentMessage>();
            while (_pendingSteering.TryDequeue(out var msg)) list.Add(msg);
            return list;
        };

        try
        {
            await foreach (var ev in _harness.RunAsync(prompt, getSteeringMessages: getSteering))
            {
                _pendingEvents.Enqueue(ev);
            }
        }
        catch (Exception ex)
        {
            _pendingEvents.Enqueue(new HarnessErrorEvent(ex.Message));
        }
    }
}