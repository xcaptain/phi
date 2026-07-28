using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace PhiCoding.Tui;

/// <summary>
/// One-line status bar: spinner + turn while running, model, cwd, and
/// token usage from the last completed turn (tau's CompactSessionInfo).
/// </summary>
public sealed class StatusLineView : View
{
    private static readonly string[] SpinnerFrames =
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly TuiState _state;
    private readonly string _model;
    private int _frame;

    public StatusLineView(TuiState state, string model)
    {
        _state = state;
        _model = model;
        CanFocus = false;
        _state.Changed += () => SetNeedsDraw();
    }

    /// <summary>Advances the spinner; called on a timer by the host app.</summary>
    public void Tick()
    {
        if (!_state.IsRunning) return;
        _frame = (_frame + 1) % SpinnerFrames.Length;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var status = _state.IsRunning
            ? $"{SpinnerFrames[_frame]} running · turn {_state.CurrentTurn}"
            : "ready";
        var usage = _state.LastUsage;
        var tokens = usage.TotalTokens > 0
            ? $" · ↑{FormatTokens(usage.Input)} ↓{FormatTokens(usage.Output)}"
            : "";

        Move(0, 0);
        SetAttributeForRole(VisualRole.Normal);
        AddStr($"{status} · {_model} · {ShortenHome(Directory.GetCurrentDirectory())}{tokens}");
        return true;
    }

    private static string FormatTokens(int n) => n switch
    {
        < 1000 => n.ToString(),
        < 1_000_000 => $"{n / 1000.0:F1}k",
        _ => $"{n / 1_000_000.0:F1}M",
    };

    private static string ShortenHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }
}
