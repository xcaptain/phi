using CommunityToolkit.Mvvm.ComponentModel;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Bottom status row: session state (idle/running), context usage,
/// model / provider label, transient error flash. UI-1 ships with
/// placeholder text; wiring subscribes to
/// <c>SessionState.IsRunning</c> / <c>SessionStatusRouter</c> /
/// <c>SessionState.ContextUsedTokens</c> and replaces placeholders
/// with live values.
/// </summary>
public partial class StatusBarViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _stateLabel = "idle";

    [ObservableProperty]
    private string _contextLabel = "0 / 8 K";

    [ObservableProperty]
    private string _modelLabel = "(no model)";
}
