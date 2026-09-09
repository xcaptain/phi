using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Phi.Providers;

namespace Phi.Avalonia.Desktop.ViewModels;

/// <summary>
/// Providers settings page. Sidebar "Providers" entry opens this page.
/// Shows every cataloged provider as a row: name + state line + masked
/// API-key TextBox + Reveal toggle + Save button. The page chrome and
/// rows are pure MVVM — no dedicated row UserControl, the row template
/// lives inline in <c>ProvidersPageView.axaml</c> as an
/// ItemsControl ItemTemplate.
/// <para>
/// UI prototype: a fresh <see cref="ProviderManager"/> per visit so the
/// page starts clean on every navigation. Phase: backend wiring will
/// inject the composition-root instance so saved keys persist across
/// navigation.
/// </para>
/// </summary>
public partial class ProvidersPageViewModel : ViewModelBase
{
    /// <summary>All provider rows in catalog display order.</summary>
    public ObservableCollection<ProviderRowState> Rows { get; }

    public ProvidersPageViewModel()
    {
        var providers = new ProviderManager();
        Rows = new ObservableCollection<ProviderRowState>(
            ProviderCatalog.All.Select(entry => new ProviderRowState(entry, providers)));
    }
}

/// <summary>
/// One provider row's state — display name + key + reveal flag + saved
/// flag + Save command. The row's UI is rendered by the parent page's
/// inline DataTemplate; this class is data-only and lives in the same
/// file as the page VM because the page is the only place rows appear.
/// </summary>
public partial class ProviderRowState : ObservableObject
{
    private readonly ProviderCatalogEntry _entry;
    private readonly ProviderManager _providers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordChar))]
    private bool _isRevealed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isSaved;

    [ObservableProperty]
    private string _key = string.Empty;

    public ProviderCatalogEntry Entry => _entry;
    public string Name => _entry.Name;
    public string DisplayName => $"{_entry.DisplayName} — {_entry.Name}";
    public char PasswordChar => IsRevealed ? default(char) : '•';
    public string StateText => IsSaved ? "✓ key saved" : "not configured";

    public ProviderRowState(ProviderCatalogEntry entry, ProviderManager providers)
    {
        _entry = entry;
        _providers = providers;
        Key = providers.ResolveApiKey(entry) ?? string.Empty;
        IsSaved = providers.HasApiKey(entry);
    }

    [RelayCommand]
    private void Save()
    {
        var trimmed = (Key ?? string.Empty).Trim();
        if (trimmed.Length == 0) return;
        _providers.SetApiKey(_entry, trimmed);
        IsSaved = true;
    }
}
