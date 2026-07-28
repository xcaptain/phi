using PhiAgent;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Bottom status bar: spinner + run state on the left, model/cwd/token usage
/// on the right. Driven by harness events via <see cref="Apply"/>.
/// </summary>
public sealed class PhiStatusBar
{
    private readonly State<int> _turn = new(0);
    private readonly State<string> _tokens = new("");

    public PhiStatusBar(string model)
    {
        Running = new State<bool>(false);

        var left = new HStack(
                new Spinner().IsActive(Running),
                new Markup(() => Running.Value ? $"running · turn {_turn.Value}" : "ready"))
            .Spacing(1);

        var right = new Markup(() =>
            $"[dim]{model} · {ShortenPath(Environment.CurrentDirectory)}{_tokens.Value}[/]");

        Visual = new StatusBar(left, right);
    }

    public Visual Visual { get; }

    public State<bool> Running { get; }

    public void Apply(HarnessEvent ev)
    {
        switch (ev)
        {
            case TurnStartEvent ts:
                Running.Value = true;
                _turn.Value = ts.Turn;
                break;
            case TurnEndEvent te:
                Running.Value = false;
                var usage = te.FinalMessage.Usage;
                _tokens.Value = usage.TotalTokens > 0
                    ? $" · ↑{FormatCount(usage.Input)} ↓{FormatCount(usage.Output)}"
                    : "";
                break;
            case HarnessErrorEvent:
                Running.Value = false;
                break;
        }
    }

    public static string ShortenPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    public static string FormatCount(int n) => n switch
    {
        < 1000 => n.ToString(),
        < 1_000_000 => $"{n / 1000.0:F1}k",
        _ => $"{n / 1_000_000.0:F1}M",
    };
}
