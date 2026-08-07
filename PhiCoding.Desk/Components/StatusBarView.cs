using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiCoding.Status;

namespace PhiCoding.Desk.Components;

/// <summary>
/// Bottom status bar. Mirrors the TUI's <c>PhiStatusBar</c>: left = run
/// state ("ready" / "running · turn N" / "ready · +K queued"), right =
/// model · cwd · token counters · context. Persistent and transient errors
/// take over the right slot and clear on the next state change with no
/// <c>LastError</c>.
/// </summary>
public sealed class StatusBarView : ISessionStatusSink
{
    private readonly ObservableValue<bool> _isRunning = new(false);
    private readonly ObservableValue<int> _turn = new(0);
    private readonly ObservableValue<int> _queuedCount = new(0);
    private readonly ObservableValue<string> _leftText = new("ready");
    private readonly ObservableValue<string> _model = new(string.Empty);
    private readonly ObservableValue<string> _tokens = new(string.Empty);
    private readonly ObservableValue<string> _context = new(string.Empty);
    private readonly ObservableValue<string> _errorText = new(string.Empty);
    private readonly ObservableValue<bool> _errorVisible = new(false);

    public StatusBarView()
    {
        var left = new Label()
            .BindText(_leftText);

        var model = new Label()
            .BindText(_model)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));

        var tokens = new Label()
            .BindText(_tokens)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));

        var context = new Label()
            .BindText(_context)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));

        var error = new Label()
            .BindText(_errorText)
            .BindIsVisible(_errorVisible)
            .TextWrapping(TextWrapping.Wrap)
            .WithTheme((t, c) => c.Foreground(DeskTheme.Danger(t)));

        // Overlay: error sits on the right when visible; otherwise model,
        // context, tokens stack in the right slot.
        var right = new Grid()
            .Columns("Auto,Auto,Auto")
            .Children(
                model.Column(0),
                context.Column(1),
                tokens.Column(2),
                error.Column(0));

        Root = new Grid()
            .Columns("Auto,*,Auto")
            .Padding(8, 4)
            .Children(
                left.Column(0),
                right.Column(2));
    }

    /// <summary>The status bar visual.</summary>
    public FrameworkElement Root { get; }

    /// <summary>Left run-state label (tests).</summary>
    internal string LeftText => _leftText.Value;

    /// <summary>Model · path label (tests).</summary>
    internal string ModelText => _model.Value;

    /// <summary>Token counter suffix (tests).</summary>
    internal string TokensText => _tokens.Value;

    /// <summary>Context counter suffix (tests).</summary>
    internal string ContextText => _context.Value;

    /// <summary>Whether an error overlay is visible (tests).</summary>
    internal bool ErrorVisible => _errorVisible.Value;

    /// <summary>Wires the bar to the session via the shared router.</summary>
    public void BindStatusBar(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        PhiCoding.Status.SessionStatusRouter.Bind(session, this);
    }

    // ──────── ISessionStatusSink ────────

    public void SetRunning(bool isRunning)
    {
        _isRunning.Value = isRunning;
        UpdateLeftText();
    }

    public void SetTurn(int turn)
    {
        _turn.Value = turn;
        UpdateLeftText();
    }

    public void SetQueuedCount(int count)
    {
        _queuedCount.Value = count;
        UpdateLeftText();
    }

    public void UpdateTokens(int inputTokens, int outputTokens)
    {
        _tokens.Value = inputTokens > 0 || outputTokens > 0
            ? $" · ↑{FormatCount(inputTokens)} ↓{FormatCount(outputTokens)}"
            : string.Empty;
    }

    public void UpdateContext(int contextUsedTokens, int? autoCompactThreshold)
    {
        _context.Value = contextUsedTokens > 0
            ? (autoCompactThreshold is { } threshold
                ? $" · {FormatCount(contextUsedTokens)}/{FormatCount(threshold + ContextWindow.DefaultCompactionReserveTokens)}"
                : $" · {FormatCount(contextUsedTokens)}")
            : string.Empty;
    }

    public void UpdateModel(string providerName, string model)
    {
        var modelPart = providerName.Length > 0 ? $"{providerName} · {model}" : model;
        _model.Value = $"{modelPart} · {ShortenPath(Environment.CurrentDirectory)}";
    }

    public void ShowError(string message, bool isPersistent)
    {
        _errorText.Value = $"⚠ {message}";
        _errorVisible.Value = true;
    }

    public void ClearError() => _errorVisible.Value = false;

    public void RecordPersistentError(string message)
    {
        // The transcript handles persistent-error line rendering; the bar's
        // ShowError already toggled the right slot, no separate action.
    }

    // ──────── Helpers ────────

    private void UpdateLeftText()
    {
        if (!_isRunning.Value && _queuedCount.Value == 0)
        {
            _leftText.Value = "ready";
            return;
        }
        if (_isRunning.Value && _queuedCount.Value > 0)
        {
            _leftText.Value = $"running · turn {_turn.Value} · +{_queuedCount.Value} queued";
            return;
        }
        if (_queuedCount.Value > 0)
        {
            _leftText.Value = $"ready · +{_queuedCount.Value} queued";
            return;
        }
        _leftText.Value = $"running · turn {_turn.Value}";
    }

    public static string FormatCount(int n) => n switch
    {
        < 1000 => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        < 1_000_000 => $"{n / 1000.0:F1}k",
        _ => $"{n / 1_000_000.0:F1}M",
    };

    public static string ShortenPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.Ordinal)
            ? "~" + path[home.Length..]
            : path;
    }
}