using PhiAgent;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Bottom status bar: spinner + run state on the left, model/cwd/cumulative
/// token usage on the right. Driven by <see cref="ISession.StateChanged"/>
/// through <see cref="UpdateStats"/>; the legacy <see cref="Apply"/> hook
/// still handles <see cref="TurnStartEvent"/> for the run indicator.
/// </summary>
public sealed class PhiStatusBar
{
    private readonly string _model;
    private readonly State<int> _turn = new(0);
    private readonly State<string> _tokens = new("");
    private readonly State<string> _context = new("");
    private readonly State<int> _queuedCount = new(0);

    public PhiStatusBar(string model)
    {
        _model = model;
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
            $"[dim]{_model} · {ShortenPath(Environment.CurrentDirectory)}{_context.Value}{_tokens.Value}[/]");

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

/// <summary>
/// Updates the cumulative token display. Called from the session's
/// <see cref="ISession.StateChanged"/> handler — invoked once on
/// resume / load (carrying prior usage) and again on each
/// <see cref="TurnEndEvent"/>.
/// </summary>
public void UpdateStats(SessionStats stats)
{
    _tokens.Value = stats.TotalTokens > 0
        ? $" · ↑{FormatCount(stats.InputTokens)} ↓{FormatCount(stats.OutputTokens)}"
        : "";
}

/// <summary>
/// Updates the live context-size / auto-compact threshold display.
/// <paramref name="contextUsedTokens"/> is the rough estimate for the
/// current request; <paramref name="autoCompactThreshold"/> is null when
/// auto-compaction is disabled or the context window is unknown.
/// </summary>
public void UpdateContext(int contextUsedTokens, int? autoCompactThreshold)
{
    if (contextUsedTokens <= 0)
    {
        _context.Value = "";
        return;
    }
    _context.Value = autoCompactThreshold is { } threshold
        ? $" · {FormatCount(contextUsedTokens)}/{FormatCount(threshold + ContextWindow.DefaultCompactionReserveTokens)}"
        : $" · {FormatCount(contextUsedTokens)}";
}

    public void Apply(HarnessEvent ev)
    {
        switch (ev)
        {
            case TurnStartEvent ts:
                Running.Value = true;
                _turn.Value = ts.Turn;
                break;
            case TurnEndEvent:
                // Token counts come from StateChanged → UpdateStats so resume
                // and ongoing turns share the same render path. The run
                // indicator still toggles here for in-flight feedback.
                Running.Value = false;
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