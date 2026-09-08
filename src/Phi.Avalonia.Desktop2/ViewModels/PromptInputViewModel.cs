using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Phi.Avalonia.Desktop2.Components;
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
/// <item><see cref="SendCommand"/>: invokes <see cref="SubmitCallback"/>
/// with <c>(text, cwd, model)</c>. The parent VM wires the callback to
/// forward the submission to <see cref="Phi.ISession.SubmitPrompt"/>;
/// when idle and the callback is null (pre-session state) the command
/// is a no-op.</item>
/// </list>
/// <para>
/// <see cref="IsWorkspaceLocked"/> flips to true once a session exists
/// (the chat page VM sets it from the bound <c>ISession</c>). The view
/// binds <c>!IsWorkspaceLocked</c> to the workspace picker's
/// <c>IsEnabled</c> so the user can't pick a different cwd mid-session.
/// <see cref="IsRunning"/> is similarly driven by
/// <c>session.State.IsRunning</c> via the parent's
/// <c>OnSessionStateChanged</c> handler.
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

/// <summary>
    /// True while a turn is in flight. Driven by the chat page VM from
    /// <c>session.State.IsRunning</c> (the parent's
    /// <c>OnSessionStateChanged</c> handler assigns this on every
    /// state change). The send button icon (ArrowUp vs Stop) and
    /// tooltip swap on this flag.
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// True once a session is bound to the chat page; the view uses it
    /// to disable the workspace picker — once a session exists, the
    /// cwd is fixed (NewSessionAsync picked it) and the picker can no
    /// longer switch workspaces mid-session.
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
    /// Workspace picker rows: distinct workspaces the user has
    /// previously chatted in (passed in as <c>knownWorkspaces</c> by
    /// the chat page VM from <c>WorkspaceSessionStore</c>), then the
    /// user's <c>$HOME</c> appended if not already present, then a
    /// trailing sentinel ("📁 Choose folder…") that opens the OS
    /// folder picker. The sentinel click handler is pending — it
    /// intentionally does nothing in this prototype so we can wire the
    /// folder picker alongside the session-creation follow-up.
    /// <para>
    /// All cwds in the picker rows are stored as
    /// <see cref="Path.GetFullPath"/>-normalized paths so comparison
    /// against <see cref="SelectedWorkspace"/> (also normalized via
    /// <see cref="DefaultWorkspace"/>) is a straight string equality
    /// no matter how the user typed the original cwd.
    /// </para>
    /// </summary>

    public ObservableCollection<WorkspacePickerItem> AvailableWorkspaces { get; }
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

    public string SendToolTip =>
        IsRunning ? "Stop" : "Send";

    /// <summary>
    /// Inverted <see cref="IsWorkspaceLocked"/> for the view's
    /// <c>IsVisible</c> binding. Avalonia XAML's <c>Binding</c> can't
    /// express logical NOT, so the VM exposes the negated value
    /// directly. The workspace picker is hidden entirely once a
    /// session is bound — there's nothing to choose, the cwd is fixed.
    public bool ShowWorkspacePicker => !IsWorkspaceLocked;

    public IRelayCommand SendCommand { get; }

    /// <summary>
    /// Callback invoked when <see cref="SendCommand"/> runs. Set by the
    /// parent VM (typically <see cref="ChatPageViewModel"/>) to forward
    /// the (text, cwd, model) triple to <see cref="Phi.ISession.SubmitPrompt"/>.
    /// The VM itself is UI-agnostic; it just fires the callback. Null
    /// until the parent wires it — when null, the Send button is a
    /// no-op (used in tests and during the pre-session prompt-input
    /// state where there's no session to submit to).
    /// </summary>
    public Action<string, string, string>? SubmitCallback { get; set; }

    /// <summary>
    /// Optional async factory the view wires up to open the OS folder
    /// picker. Called when the user picks the trailing
    /// "📁 Choose folder…" sentinel row in the workspace picker.
    /// Returns the absolute path the user chose, or null when they
    /// cancelled. The VM is UI-agnostic so it goes through this seam
    /// instead of touching Avalonia's StorageProvider directly; tests
    /// substitute a stub that returns a fixed path.
    /// </summary>
    public Func<Task<string?>>? ChooseFolderProvider { get; set; }

    public PromptInputViewModel(
        ProviderManager providers,
        IReadOnlyList<string>? knownWorkspaces = null)
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

        // Workspace picker rows: distinct known workspaces from the
        // store + $HOME (deduped) + the Choose-folder sentinel. The
        // chat page VM reads the list from WorkspaceSessionStore and
        // passes it in; null here means "no prior workspaces" (tests
        // the sentinel so the picker is never just two rows.
        AvailableWorkspaces = new ObservableCollection<WorkspacePickerItem>(
            BuildAvailableWorkspaces(_selectedWorkspace, knownWorkspaces));

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
            execute: () =>
            {
                // Mirror canExecute in the body — IRelayCommand.Execute
                // doesn't enforce CanExecute itself, so an empty-text
                // submit would still fire the callback if the button
                // is wired directly (e.g. keyboard ⏎ via IsDefault on
                // the Button). Only submit when there's something to
                // send, or when the user is mid-turn and the button is
                // the cancel glyph.
                if (!CanSubmit()) return;
                SubmitCallback?.Invoke(Text, SelectedWorkspace, SelectedModel ?? string.Empty);
            },
            canExecute: CanSubmit);

        // Single source of truth for "the button should be live".
        // Re-evaluated when Text or IsRunning flips because both are
        // [NotifyCanExecuteChangedFor(SendCommand)] or the property
        // generator picks them up via the SendCommand setter.
    }

    private bool CanSubmit() =>
        !string.IsNullOrWhiteSpace(Text) || IsRunning;

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
    /// Three-segment workspace picker:
    /// <list type="number">
    /// <item>Distinct workspaces from <paramref name="knownWorkspaces"/>
    /// (the chat page VM passes <c>WorkspaceSessionStore.ListWorkspaces()</c>),
    /// in the order the caller supplied — typically newest activity
    /// first.</item>
    /// <item>The user's <c>$HOME</c>, appended if not already covered
    /// by a known workspace (case-insensitive dedupe so
    /// <c>/Users/me</c> and <c>/Users/Me</c> collapse).</item>
    /// <item>The trailing sentinel ("📁 Choose folder…") that opens
    /// the OS folder picker — sentinel click handler pending.</item>
    /// </list>
    /// Each row's <see cref="WorkspacePickerItem.Cwd"/> is
    /// <see cref="Path.GetFullPath"/>-normalized so equality with
    /// <see cref="SelectedWorkspace"/> (also normalized via
    /// <see cref="DefaultWorkspace"/>) is a plain string compare.
    /// <see cref="WorkspacePickerItem.Label"/> uses the existing
    /// <see cref="Phi.Avalonia.Desktop2.Components.NavModel.WorkspaceLabel"/>
    /// helper so paths inside <c>$HOME</c> render as
    /// <c>~/foo/bar</c> instead of the full path.
    /// </summary>
    private static List<WorkspacePickerItem> BuildAvailableWorkspaces(
        string cwd,
        IReadOnlyList<string>? knownWorkspaces)
    {
        var list = new List<WorkspacePickerItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentFull = Path.GetFullPath(cwd);

        if (knownWorkspaces is not null)
        {
            foreach (var w in knownWorkspaces)
            {
                if (string.IsNullOrEmpty(w)) continue;
                var full = Path.GetFullPath(w);
                if (!seen.Add(full)) continue;
                list.Add(new WorkspacePickerItem(
                    Label: NavModel.WorkspaceLabel(full),
                    Cwd: full,
                    IsCurrent: string.Equals(full, currentFull, StringComparison.OrdinalIgnoreCase),
                    IsSentinel: false));
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            var fullHome = Path.GetFullPath(home);
            if (seen.Add(fullHome))
            {
                list.Add(new WorkspacePickerItem(
                    Label: NavModel.WorkspaceLabel(fullHome),
                    Cwd: fullHome,
                    IsCurrent: string.Equals(fullHome, currentFull, StringComparison.OrdinalIgnoreCase),
                    IsSentinel: false));
            }
        }

        list.Add(new WorkspacePickerItem(
            Label: "📁 Choose folder…",
            Cwd: string.Empty,
            IsCurrent: false,
            IsSentinel: true));

        return list;
    }

    /// <summary>Home directory, used as the default cwd when the prompt
    /// input mounts. Normalized via <see cref="Path.GetFullPath"/> so
    /// the picker row's <see cref="WorkspacePickerItem.Cwd"/> matches
    /// exactly (no trailing slash, no <c>./</c> segments). Falls back
    /// to <see cref="Environment.CurrentDirectory"/> on platforms where
    /// <c>SpecialFolder.UserProfile</c> is empty.</summary>
    private static string DefaultWorkspace()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cwd = string.IsNullOrEmpty(home)
            ? Environment.CurrentDirectory
            : home;
        return Path.GetFullPath(cwd);
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
        SyncSelectedItemToWorkspace(value);
    }

    /// <summary>Mirror a ComboBox row click into <see cref="SelectedWorkspace"/>.
    /// Two cases:
    /// <list type="bullet">
    /// <item>Sentinel row → revert the visual selection to the current
    /// cwd row, then async-kick the OS folder picker via
    /// <see cref="ChooseFolderProvider"/>. The picker result, if any,
    /// is normalised and written back through <see cref="SelectedWorkspace"/>
    /// which routes through <see cref="OnSelectedWorkspaceChanged"/> to
    /// re-highlight the matching row.</item>
    /// <item>Regular row → copy its <see cref="WorkspacePickerItem.Cwd"/>
    /// into <see cref="SelectedWorkspace"/>. The setter detects a
    /// no-op when the value is unchanged and skips the cascade.</item>
    /// </list>
    /// Without this handler the ComboBox selection is purely cosmetic
    /// — the picker row's click never reached <see cref="SelectedWorkspace"/>
    /// so <c>HandleSubmit</c> would submit the stale default cwd.</summary>
    partial void OnSelectedWorkspaceItemChanged(WorkspacePickerItem? value)
    {
        if (value is { IsSentinel: true })
        {
            // Sentinel = "open the OS folder picker". The sentinel's
            // own Cwd is empty so we don't write it into SelectedWorkspace;
            // instead we revert the visual ComboBox selection to the
            // current cwd row (so the user doesn't see the sentinel
            // visually stuck as the selection while the dialog is up)
            // and kick off the async provider.
            SyncSelectedItemToWorkspace();
            if (ChooseFolderProvider is not null)
                _ = PickFolderAsync(SelectedWorkspace);
            return;
        }

        if (value is { Cwd: var cwd } && !string.IsNullOrEmpty(cwd))
            SelectedWorkspace = cwd;
    }

    /// <summary>Force the ComboBox's <see cref="SelectedWorkspaceItem"/>
    /// to track <see cref="SelectedWorkspace"/>. Shared between the
    /// sentinel snap-back and the normal <c>SelectedWorkspace</c> setter
    /// so both paths land on the same matching logic. Re-fires
    /// <see cref="ObservableProperty.INotifyPropertyChanged"/> when the
    /// row actually changes so the ComboBox re-renders (the sentinel
    /// snap-back path mutates the field from inside the setter's
    /// callback, so the auto-notify from the original setter already
    /// fired with the sentinel value — we need a second notify to
    /// push the reverted row back). <paramref name="cwdOverride"/>
    /// exists so callers that already have the new cwd in hand (the
    /// <c>OnSelectedWorkspaceChanged</c> partial) can skip the
    /// property read.</summary>
    private void SyncSelectedItemToWorkspace(string? cwdOverride = null)
    {
        var cwd = cwdOverride ?? SelectedWorkspace;
        var match = AvailableWorkspaces.FirstOrDefault(
            w => !w.IsSentinel
                && string.Equals(w.Cwd, cwd, StringComparison.OrdinalIgnoreCase));
        if (ReferenceEquals(SelectedWorkspaceItem, match)) return;
        // Direct field write to skip the OnSelectedWorkspaceItemChanged
        // partial — that handler is the caller in the sentinel snap-back
        // path and re-entering it would loop back into SyncSelectedItemToWorkspace.
        // OnPropertyChanged is fired manually because the generated setter's
        // auto-notify already fired with the previous (e.g. sentinel) value
        // before this helper runs.
#pragma warning disable MVVMTK0034
        _selectedWorkspaceItem = match;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SelectedWorkspaceItem));
    }

    /// <summary>Awaits the OS folder picker and applies the result to
    /// <see cref="SelectedWorkspace"/> only when the user hasn't
    /// changed their mind since the dialog opened. <c>beforeCwd</c>
    /// captures the cwd that was active when the sentinel was clicked;
    /// if <see cref="SelectedWorkspace"/> has drifted away from it
    /// (the user picked a different row, or a state-change path
    /// updated it) we drop the picker result on the floor so we don't
    /// clobber the user's later choice. Exceptions from the provider
    /// are logged via stderr — the prototype has no status bar slot
    /// to surface them yet.</summary>
    private async Task PickFolderAsync(string beforeCwd)
    {
        try
        {
            var picked = await ChooseFolderProvider!().ConfigureAwait(true);
            if (string.IsNullOrEmpty(picked)) return;
            if (!string.Equals(SelectedWorkspace, beforeCwd, StringComparison.Ordinal))
                return;
            var full = Path.GetFullPath(picked);

            // Insert the picked cwd as a new row so the ComboBox can
            // highlight it after OnSelectedWorkspaceChanged runs
            // SyncSelectedItemToWorkspace. Without this the row
            // wouldn't exist yet and the ComboBox would fall back to
            // its "Select workspace" placeholder (looks like the
            // pick silently failed). We keep the sentinel as the
            // trailing row by inserting before it.
            var existing = AvailableWorkspaces.FirstOrDefault(w =>
                string.Equals(w.Cwd, full, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var sentinelIndex = AvailableWorkspaces.Count - 1;
                AvailableWorkspaces.Insert(sentinelIndex, new WorkspacePickerItem(
                    Label: NavModel.WorkspaceLabel(full),
                    Cwd: full,
                    IsCurrent: true,
                    IsSentinel: false));
            }

            SelectedWorkspace = full;
        }
        catch (Exception ex)
        {
            // OS picker can throw on platform-specific failures
            // (dialog cancellation re-raised, COM errors on Windows,
            // etc.). Swallow + log; user can retry by clicking the
            // sentinel again.
            System.Console.Error.WriteLine($"PickFolderAsync: {ex.Message}");
        }
    }

    /// <summary>Called from the VM's partial property generator so
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SendIconKind));
        OnPropertyChanged(nameof(SendToolTip));
    }

    /// <summary>Called from the VM's partial property generator so
    /// <see cref="ShowWorkspacePicker"/> re-evaluates when
    /// <see cref="IsWorkspaceLocked"/> flips. <see cref="ShowWorkspacePicker"/>
    /// is a computed property so it doesn't auto-notify; this partial
    /// does it manually when the underlying flag changes.</summary>
    partial void OnIsWorkspaceLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWorkspacePicker));
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
