using PhiAgent;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PhiCoding.Tui;

/// <summary>
/// Phi TUI: single-session, turn-based, full-screen terminal interface for
/// the Phi harness, built on Terminal.Gui v2. Layout:
///
///   ┌─ status line (spinner · turn · model · cwd · tokens) ─┐
///   ├─ transcript (scrollable, colored, follows output)     ┤
///   ├─ prompt (multi-line; Enter submits, Shift+Enter NL)   ┤
///   └─ key hints ───────────────────────────────────────────┘
///
/// The agent runs on a background task; each <see cref="HarnessEvent"/> is
/// marshaled to the UI thread via <c>IApplication.Invoke</c> and projected
/// into <see cref="TuiState"/> by <see cref="TuiEventAdapter"/>. While the
/// agent runs the prompt is read-only (turn-based input model).
/// </summary>
public sealed class PhiTuiApp
{
    private readonly Harness _harness;
    private readonly string _model;
    private readonly TuiState _state = new();

    public PhiTuiApp(Harness harness, string model)
    {
        _harness = harness;
        _model = model;
    }

    public void Run()
    {
        using IApplication app = Application.Create();
        app.Init();

        using var window = new Window { Title = "phi (Esc to quit)" };

        var status = new StatusLineView(_state, _model)
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = 1,
        };
        var transcript = new TranscriptView(_state)
        {
            X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(5),
        };
        var prompt = new PromptInput
        {
            X = 0, Y = Pos.AnchorEnd(5), Width = Dim.Fill(), Height = 4,
        };
        var hint = new Label
        {
            X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1,
            Text = "Enter submit · Shift+Enter newline · Tab switch focus · PgUp/PgDn scroll",
        };

        window.Add(status, transcript, prompt, hint);

        prompt.Submitted += text => OnSubmitted(app, prompt, text);
        _state.Changed += () =>
        {
            var running = _state.IsRunning;
            if (prompt.ReadOnly != running) prompt.ReadOnly = running;
            if (!running) prompt.SetFocus();
        };

        app.AddTimeout(TimeSpan.FromMilliseconds(120), () =>
        {
            status.Tick();
            return true;
        });

        prompt.SetFocus();
        app.Run(window);
    }

    private void OnSubmitted(IApplication app, PromptInput prompt, string text)
    {
        if (_state.IsRunning) return;
        _state.AddUserMessage(text);
        _ = Task.Run(() => RunAgentAsync(app, text));
    }

    private async Task RunAgentAsync(IApplication app, string prompt)
    {
        try
        {
            await foreach (var ev in _harness.RunAsync(prompt))
                app.Invoke(() => TuiEventAdapter.Apply(_state, ev));
        }
        catch (Exception ex)
        {
            app.Invoke(() => _state.AddError(ex.Message));
        }
    }
}
