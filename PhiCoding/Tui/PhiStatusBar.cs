using System.Globalization;
using PhiAgent;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Bottom status bar: spinner + run state on the left, model/cwd/cumulative
/// token usage on the right. Transient session errors take over the right
/// slot via <see cref="ShowError"/> and clear on the next
/// <see cref="ISession.StateChanged"/> event. Driven by
/// <see cref="ISession.StateChanged"/> through <see cref="UpdateStats"/>; the
/// legacy <see cref="Apply"/> hook still handles <see cref="TurnStartEvent"/>
/// for the run indicator.
/// </summary>
public sealed class PhiStatusBar
{
    // Model/provider are State<T> (not plain strings) so the dynamic right
    // Markup's dependency tracking invalidates and re-renders when a /models
    // or /connect switch updates them — the same mechanism that drives the
    // running/token/context labels.
    private readonly State<string> _model;
    private readonly State<string> _providerName = new("");
    private readonly State<int> _turn = new(0);
    private readonly State<string> _tokens = new("");
    private readonly State<string> _context = new("");
    private readonly State<int> _queuedCount = new(0);
    private readonly State<ErrorDisplay?> _currentError = new(null);

    public PhiStatusBar(string model)
    {
        _model = new State<string>(model);
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

        var right = new Markup(() => BuildRightText());

        Visual = new StatusBar(left, right);
    }

    public Visual Visual { get; }

    public State<bool> Running { get; }

    /// <summary>
    /// Updates the provider · model label shown in the right slot. Called
    /// from the session's <see cref="ISession.StateChanged"/> handler so a
    /// <c>/connect</c> or <c>/models</c> switch reflects immediately.
    /// </summary>
    public void UpdateModel(string providerName, string model)
    {
        _providerName.Value = providerName;
        _model.Value = model;
    }

    /// <summary>
    /// Counter showing how many user-submitted messages are waiting to be
    /// drained by the run loop. Set externally via
    /// <see cref="UpdateStats"/> from the session's
    /// <see cref="ISession.StateChanged"/> handler.
    /// </summary>
    public State<int> QueuedCount => _queuedCount;

    /// <summary>
    /// Currently displayed error, if any. Visible for inspection and tests.
    /// </summary>
    public ErrorDisplay? CurrentError => _currentError.Value;

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

    /// <summary>
    /// Shows an error message in the right slot of the status bar.
    /// <paramref name="isPersistent"/> chooses the highlight color: persistent
    /// errors render red, transient (network blip, retry) render yellow.
    /// The next <see cref="ClearError"/> call (driven by a state change in
    /// <see cref="PhiTuiApp"/>) restores the model/path/tokens display.
    /// </summary>
    public void ShowError(string message, bool isPersistent)
    {
        ArgumentNullException.ThrowIfNull(message);
        _currentError.Value = new ErrorDisplay(message, isPersistent);
    }

    /// <summary>
    /// Removes any active error and restores the model/path/tokens display.
    /// Called by <see cref="PhiTuiApp"/> on every state change that does
    /// not carry a new <c>LastError</c>.
    /// </summary>
    public void ClearError() => _currentError.Value = null;

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
        < 1000 => n.ToString(CultureInfo.InvariantCulture),
        < 1_000_000 => $"{n / 1000.0:F1}k",
        _ => $"{n / 1_000_000.0:F1}M",
    };

    private string BuildRightText()
    {
        var err = _currentError.Value;
        if (err is not null)
        {
            var color = err.IsPersistent ? "red" : "yellow";
            return $"[{color}]⚠ {Escape(err.Message)}[/]";
        }
        var label = _providerName.Value.Length > 0
            ? $"{_providerName.Value} · {_model.Value}"
            : _model.Value;
        return $"[dim]{label} · {ShortenPath(Environment.CurrentDirectory)}{_context.Value}{_tokens.Value}[/]";
    }

    private static string Escape(string text) => text.Replace("[", "\\[").Replace("]", "\\]");

    /// <summary>Active error display state. <see cref="IsPersistent"/> drives the color.</summary>
    public sealed record ErrorDisplay(string Message, bool IsPersistent);
}
