using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Bottom-row input editor for the chat. Holds:
/// <list type="bullet">
/// <item><see cref="Text"/>: the editor's current draft.</item>
/// <item><see cref="SelectedWorkspace"/>: the cwd the next session will
/// be created in. Default is the user's home directory; the workspace
/// picker surfaces it through <see cref="AvailableWorkspaces"/>.
/// </item>
/// <item><see cref="AvailableModels"/>: catalog of models from providers
/// that have API keys configured (see <see cref="ProviderManager.HasApiKey"/>).
/// Each row carries a <see cref="ModelPickerItem"/> so the data template
/// can style the current model.</item>
/// <item><see cref="SelectedModel"/>: defaults to the first connected
/// provider's <see cref="ProviderCatalogEntry.DefaultModel"/>.</item>
/// <item><see cref="AvailableWorkspaces"/>: the picker rows — the current
/// cwd + a trailing sentinel row (<see cref="WorkspacePickerItem.IsSentinel"/>)
/// that the old layout uses to open the OS folder picker. The sentinel
/// click behaviour is intentionally not wired in this prototype.</item>
/// <item><see cref="SendCommand"/>: today a no-op toggle for
/// <see cref="IsRunning"/> so the UI prototype shows the icon flip; the
/// wiring phase replaces the body with
/// <c>session.SubmitPrompt(text)</c> / <c>session.Cancel()</c>.</item>
/// </list>
/// <para>
/// <see cref="IsWorkspaceLocked"/> flips to true once a session exists
/// (the wiring phase sets it). The view binds <c>!IsWorkspaceLocked</c>
/// to the workspace picker's <c>IsEnabled</c> so the user can't pick a
/// different cwd mid-session.
/// </para>
/// </summary>
public partial class PromptInputViewModel : ViewModelBase
{
    private readonly ProviderManager _providers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _text = "";

    [ObservableProperty]
    private string _selectedWorkspace = DefaultWorkspace();

    [ObservableProperty]
    private string? _selectedModel;

    /// <summary>
    /// ComboBox-bound selected item for the model picker. TwoWay bound
    /// from the XAML — writing it back updates <see cref="SelectedModel"/>
    /// via the partial setter below. Nullable so the placeholder
    /// ("Select model") shows when nothing is selected.
    /// </summary>
    [ObservableProperty]
    private ModelPickerItem? _selectedModelItem;

    /// <summary>
    /// ComboBox-bound selected item for the workspace picker. TwoWay
    /// bound from the XAML. Nullable so picking the sentinel row clears
    /// <see cref="SelectedWorkspace"/> rather than coercing the sentinel
    /// into a real cwd.
    /// </summary>
    [ObservableProperty]
    private WorkspacePickerItem? _selectedWorkspaceItem;

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// True while a turn is in flight. Toggled by <see cref="SendCommand"/>
    /// in the UI prototype; the wiring phase will mirror
    /// <c>session.State.IsRunning</c> instead.
    /// </summary>
    [ObservableProperty]
    private bool _isWorkspaceLocked;

    /// <summary>
    /// Catalog of models from providers with API keys configured. Empty
    /// when no provider is connected; the picker then shows the
    /// placeholder text from <see cref="ModelPickerPlaceholder"/>.
    /// </summary>
    public IReadOnlyList<ModelPickerItem> AvailableModels { get; }

    /// <summary>
    /// Workspace picker rows: the current cwd plus a trailing sentinel
    /// ("📁 Choose folder…"). The sentinel click handler is pending —
    /// it intentionally does nothing in this prototype so we can wire
    /// the folder picker alongside the session-creation follow-up.
    /// </summary>
    public IReadOnlyList<WorkspacePickerItem> AvailableWorkspaces { get; }

    /// <summary>
    /// Placeholder text for the model picker. "Select model" when at
    /// least one provider is connected; otherwise a hint that says the
    /// user should save an API key in the Providers page.
    /// </summary>
    public string ModelPickerPlaceholder
        => AvailableModels.Count == 0
            ? "no providers connected — go to Providers"
            : "Select model";

    /// <summary>
    /// Send-button icon. UpArrow when idle (user can hit send), Stop
    /// (filled square) when a turn is running (user can cancel). Single
    /// binding drives the icon swap.
    /// </summary>
    public MaterialIconKind SendIconKind =>
        IsRunning ? MaterialIconKind.Stop : MaterialIconKind.ArrowUpward;

    /// <summary>Tooltip + accessibility label for the send button.</summary>
    public string SendToolTip =>
        IsRunning ? "Stop" : "Send";

    public IRelayCommand SendCommand { get; }

    public PromptInputViewModel(ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;

        AvailableModels = BuildAvailableModels(_providers);

        // Default selected model = first connected provider's DefaultModel.
        var firstConnected = ProviderCatalog.All
            .Where(_providers.HasApiKey)
            .Cast<ProviderCatalogEntry?>()
            .FirstOrDefault();
        if (firstConnected is { } defaultEntry)
            _selectedModel = defaultEntry.DefaultModel;

        // Workspace picker rows: the current cwd + a trailing sentinel.
        // The current-cwd row is marked IsCurrent so the data template
        // can bold it; the sentinel is the last row and is dimmed.
        AvailableWorkspaces = BuildAvailableWorkspaces(_selectedWorkspace);

        // Resolve the ComboBox-bound *Item properties to the matching
        // picker row so the ComboBox highlights the right entry on
        // first render. We don't assign via the property setters from
        // the constructor because ObservableProperty's setters run
        // OnPropertyChanged; the partial setters are idempotent so
        // it's safe, but the direct field assignment matches the
        // _selectedModel / _selectedWorkspace initialisation above.
        _selectedModelItem = AvailableModels.FirstOrDefault(m => m.Label == _selectedModel);
        _selectedWorkspaceItem = AvailableWorkspaces.FirstOrDefault(w => w.IsCurrent);

        SendCommand = new RelayCommand(
            execute: () => IsRunning = !IsRunning,
            canExecute: () => !string.IsNullOrWhiteSpace(Text) || IsRunning);
    }

    /// <summary>
    /// Distinct model names from every catalog entry whose API key is
    /// configured, in catalog display order. Each entry carries an
    /// <see cref="ModelPickerItem.IsCurrent"/> flag set when the model
    /// matches <see cref="SelectedModel"/> so the data template can bold
    /// the current row in the dropdown. Returns the concrete
    /// <see cref="List{T}"/> (not <see cref="IReadOnlyList{T}"/>) so the
    /// CA1859 analyzer doesn't flag the conversion done by the public
    /// <see cref="AvailableModels"/> property.
    /// </summary>
    private static List<ModelPickerItem> BuildAvailableModels(ProviderManager providers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var entry in ProviderCatalog.All)
        {
            if (!providers.HasApiKey(entry)) continue;
            foreach (var model in entry.Models)
            {
                if (seen.Add(model))
                    result.Add(model);
            }
        }
        return result
            .Select(name => new ModelPickerItem(Label: name, IsCurrent: false))
            .ToList();
    }

    /// <summary>
    /// Two-row workspace picker: the current cwd, then a trailing sentinel
    /// ("📁 Choose folder…") that the old layout uses to open the OS
    /// folder picker. The sentinel click handler is intentionally not
    /// wired in this prototype; the workspace picker therefore can't
    /// change <see cref="SelectedWorkspace"/> through the ComboBox yet.
    /// The user can still edit the cwd via a follow-up that replaces
    /// this picker with the old code's folder-picker sentinel flow.
    /// </summary>
    private static IReadOnlyList<WorkspacePickerItem> BuildAvailableWorkspaces(string cwd)
    {
        return
        [
            new WorkspacePickerItem(
                Label: cwd,
                Cwd: cwd,
                IsCurrent: true,
                IsSentinel: false),
            new WorkspacePickerItem(
                Label: "📁 Choose folder…",
                Cwd: string.Empty,
                IsCurrent: false,
                IsSentinel: true),
        ];
    }

    /// <summary>Home directory, used as the default cwd when the prompt
    /// input mounts. Falls back to <see cref="Environment.CurrentDirectory"/>
    /// on platforms where <c>SpecialFolder.UserProfile</c> is empty.</summary>
    private static string DefaultWorkspace()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home)
            ? Environment.CurrentDirectory
            : home;
    }

    /// <summary>Re-evaluate IsCurrent flags when the selection changes,
    /// so the data template's bolding follows the live
    /// <see cref="SelectedModel"/>.</summary>
    partial void OnSelectedModelChanged(string? value)
    {
        // Keep SelectedModelItem in sync so the ComboBox's
        // SelectedItem reflects the live selection. The picker row
        // data is constructed once and immutable, so we look the
        // matching label up.
        var match = AvailableModels.FirstOrDefault(m => m.Label == value);
        if (!ReferenceEquals(_selectedModelItem, match))
            _selectedModelItem = match;
    }

    /// <summary>Keep <see cref="SelectedWorkspaceItem"/> in sync with
    /// <see cref="SelectedWorkspace"/> so the ComboBox's SelectedItem
    /// follows the cwd. The sentinel row has an empty Cwd; when
    /// nothing matches we clear <see cref="SelectedWorkspaceItem"/>
    /// so the ComboBox falls back to its placeholder.</summary>
    partial void OnSelectedWorkspaceChanged(string value)
    {
        var match = AvailableWorkspaces.FirstOrDefault(
            w => !w.IsSentinel && string.Equals(w.Cwd, value, StringComparison.Ordinal));
        if (!ReferenceEquals(_selectedWorkspaceItem, match))
            _selectedWorkspaceItem = match;
    }

    /// <summary>Called from the VM's partial property generator so
    /// <see cref="SendIconKind"/> / <see cref="SendToolTip"/> re-evaluate
    /// when <see cref="IsRunning"/> flips.</summary>
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SendIconKind));
        OnPropertyChanged(nameof(SendToolTip));
    }
}

/// <summary>
/// One row in the model picker. <see cref="Label"/> is the rendered text;
/// <see cref="IsCurrent"/> is the visual hint for the data template (the
/// old layout bolds the current row). The plain-string
/// <see cref="PromptInputViewModel.SelectedModel"/> is the source of
/// truth — the data template compares against it.
/// </summary>
public sealed record ModelPickerItem(string Label, bool IsCurrent);

/// <summary>
/// One row in the workspace picker. <see cref="Label"/> is the rendered
/// text; <see cref="Cwd"/> is the path that becomes
/// <see cref="PromptInputViewModel.SelectedWorkspace"/> when the row is
/// selected; <see cref="IsCurrent"/> marks the current cwd; the sentinel
/// row carries <see cref="IsSentinel"/> = true and an empty
/// <see cref="Cwd"/>. The sentinel click behaviour is intentionally not
/// wired in this prototype.
/// </summary>
public sealed record WorkspacePickerItem(
    string Label,
    string Cwd,
    bool IsCurrent,
    bool IsSentinel);

/// <summary>
/// Static converters used by the picker DataTemplates to style the
/// current row (SemiBold) versus normal rows (Normal). Lives at
/// namespace scope (not nested under <see cref="PromptInputViewModel"/>)
/// because the Avalonia XAML compiler does not support
/// <c>OuterClass+NestedClass</c> lookup in <c>x:Static</c> — a follow-up
/// that ships per-picker styling (e.g. the sentinel dim colour) can
/// extend this class without introducing a new converter file.
/// </summary>
public static class ModelPickerItemStyles
{
    /// <summary>
    /// <c>True</c> → <see cref="FontWeight.SemiBold"/>;
    /// <c>False</c> → <see cref="FontWeight.Normal"/>. The data template
    /// binds <c>FontWeight</c> on the row TextBlock through this
    /// converter so the current model is visually anchored in the
    /// dropdown. Named <c>WeightConverter</c> (not <c>FontWeight</c>) so
    /// the property name doesn't shadow the <see cref="FontWeight"/>
    /// enum value referenced in its body.
    /// </summary>
    public static readonly IValueConverter WeightConverter =
        new FuncValueConverter<bool, FontWeight>(
            current => current ? FontWeight.SemiBold : FontWeight.Normal);
}

/// <summary>
/// Static converters used by the workspace picker DataTemplate — same
/// pattern as <see cref="ModelPickerItemStyles"/> but on
/// <see cref="WorkspacePickerItem"/>.
/// </summary>
public static class WorkspacePickerItemStyles
{
    public static readonly IValueConverter WeightConverter =
        new FuncValueConverter<bool, FontWeight>(
            current => current ? FontWeight.SemiBold : FontWeight.Normal);
}