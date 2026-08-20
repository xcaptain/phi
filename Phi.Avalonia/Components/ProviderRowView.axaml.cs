using Avalonia.Controls;
using Avalonia.Input;
using Phi.Providers;

namespace Phi.Avalonia.Components;

/// <summary>
/// One row in the Providers settings list. Renders the provider's name +
/// state, an inline masked <see cref="TextBox"/> for the API key, a
/// Reveal toggle, and a Save button. The row holds a reference to its
/// <see cref="ProviderCatalogEntry"/> and the <see cref="ProviderManager"/>
/// it should save to; saving persists the trimmed key and updates the
/// state label without rebuilding the row.
/// </summary>
public sealed partial class ProviderRowView : UserControl
{
    private readonly ProviderCatalogEntry? _entry;
    private readonly ProviderManager? _providers;

    // Parameterless constructor keeps the AXAML source generator happy
    // (it emits AVLN3001 if no parameterless ctor is reachable). The row
    // is only ever constructed from C# via the entry + providers overload
    // below; this ctor exists for the rare XAML-loader path. Callers that
    // hit it never get a working row — they should use the parameterized
    // overload instead.
    public ProviderRowView() : this(null!, null!)
    {
    }

    public ProviderRowView(ProviderCatalogEntry entry, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(providers);
        _entry = entry;
        _providers = providers;

        InitializeComponent();

        NameLabel.Text = $"{entry.DisplayName} — {entry.Name}";
        KeyInput.FontFamily = AvaloniaTheme.MonoFontFamily;
        // Avalonia TextBox defaults to null; force "" so consumers see an
        // empty string when the row is freshly built (and after the user
        // clears the field).
        KeyInput.Text = string.Empty;
        var existingKey = providers.ResolveApiKey(entry);
        if (existingKey is { Length: > 0 })
            KeyInput.Text = existingKey;

        ApplyState(providers.HasApiKey(entry));

        RevealToggle.IsCheckedChanged += (_, _) =>
            KeyInput.RevealPassword = RevealToggle.IsChecked ?? false;
        KeyInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Save();
                e.Handled = true;
            }
        };
        SaveButton.Click += (_, _) => Save();
    }

    /// <summary>The provider catalog entry this row represents.</summary>
    public ProviderCatalogEntry? Entry => _entry;

    private void Save()
    {
        var key = (KeyInput.Text ?? string.Empty).Trim();
        if (key.Length == 0) return;
        if (_entry is null || _providers is null) return;
        _providers.SetApiKey(_entry, key);
        ApplyState(saved: true);
    }

    private void ApplyState(bool saved)
    {
        StateLabel.Text = saved ? "✓ key saved" : "not configured";
        StateLabel.Foreground = saved ? AvaloniaTheme.Success : AvaloniaTheme.TextSecondary;
    }
}