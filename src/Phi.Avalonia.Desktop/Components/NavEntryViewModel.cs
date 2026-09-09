using CommunityToolkit.Mvvm.ComponentModel;

namespace Phi.Avalonia.Desktop.Components;

/// <summary>
/// One row in the sidebar's entries list. The list holds a flat
/// sequence of either <see cref="WorkspaceNavEntryViewModel"/>
/// (group title) or <see cref="SessionNavEntryViewModel"/>
/// (chat session). Both kinds share a base class so the
/// <c>ItemsControl</c>'s outer <c>DataTemplate</c> can pass through
/// to a per-kind inner template. Inherits
/// <see cref="ObservableObject"/> so the session-row
/// <c>IsActive</c> property is observable (the workspace-row variant
/// has no observable state of its own).
/// </summary>
public abstract class NavEntryViewModel : ObservableObject
{
    /// <summary>Display label for the row (upper-cased for workspace
    /// headers via <see cref="NavModel.WorkspaceLeafLabel"/>; verbatim
    /// for sessions via <see cref="NavModel.TitleOf"/>).</summary>
    public string Title { get; }

    protected NavEntryViewModel(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }
}

/// <summary>A workspace header row in the sessions list. Click is a
/// no-op — these rows are group titles, not navigable entries.
/// Future work adds a ⋯ menu with New Session / Delete actions.</summary>
public sealed class WorkspaceNavEntryViewModel : NavEntryViewModel
{
    /// <summary>The full working directory the workspace group
    /// represents. Kept on the row so a future menu action (e.g.
    /// New Session in workspace) has the cwd to pass through.</summary>
    public string Cwd { get; }

    public WorkspaceNavEntryViewModel(string title, string cwd)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(cwd);
        Cwd = cwd;
    }
}

/// <summary>A session row in the sessions list. Carries
/// <see cref="SessionId"/> so the row click command can resume the
/// right session, plus an <see cref="IsActive"/> flag bound from the
/// sidebar's <c>RebuildNav</c> pass — the row's visual state (accent
/// fill + bold title) is driven entirely from this flag.</summary>
public partial class SessionNavEntryViewModel : NavEntryViewModel
{
    public string SessionId { get; }

    [ObservableProperty]
    private bool _isActive;

    public SessionNavEntryViewModel(string title, string sessionId, bool isActive)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        SessionId = sessionId;
        IsActive = isActive;
    }
}
