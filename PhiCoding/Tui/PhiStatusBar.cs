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
    private readonly State<int> _queuedCount = new(0);

    public PhiStatusBar(string model)
    {
        Running = new State<bool>(false);

        var left = new HStack(
                new Spinner().IsActive(Running),
                new Markup(() =>
                {
                    if (!Running.Value && _queuedCount.Value == 0)
                        return "ready";
                    if (Running.Value && _queuedCount.Value > 0)
                        return $"running · turn {_turn.Value} · +{_queuedCount.Value} queued";
                    if (_queuedCount.Value > 0)
                        return $"ready · +{_queuedCount.Value} queued";
                    return $"running · turn {_turn.Value}";
                }))
            .Spacing(1);

        var right = new Markup(() =>
            $"[dim]{model} · {ShortenPath(Environment.CurrentDirectory)}{_tokens.Value}[/]");

        Visual = new StatusBar(left, right);
    }

    public Visual Visual { get; }

    public State<bool> Running { get; }

    /// <summary>
    /// Bind to a <see cref="MessageQueue"/> (or a combined steering+follow-up
    /// counter) so the bar shows how many user-submitted messages are waiting
    /// to be drained.
    /// </summary>
    public State<int> QueuedCount => _queuedCount;

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
