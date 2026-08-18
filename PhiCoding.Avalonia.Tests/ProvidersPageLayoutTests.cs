using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using PhiCoding.Avalonia.Components;
using PhiCoding.Providers;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ProvidersPageLayout"/>: pure-declarative chrome — the
/// header text + a named slot for per-provider rows.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ProvidersPageLayoutTests
{
    [Test]
    public async Task Root_IsUserControl_WrappingScrollViewer()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ProvidersPageLayout();
        await Assert.That(layout).IsAssignableTo<UserControl>();
        await Assert.That(layout.Content).IsAssignableTo<ScrollViewer>();
    }

    [Test]
    public async Task Header_HasProvidersTitleAndHint()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ProvidersPageLayout();
        var panel = (StackPanel)((ScrollViewer)layout.Content!).Content!;
        await Assert.That(((TextBlock)panel.Children[0]).Text).IsEqualTo("Providers");
        await Assert.That(((TextBlock)panel.Children[1]).Text).Contains("API key");
    }

    [Test]
    public async Task RowsHost_IsAnEmptyStackPanel_Slot()
    {
        // The page populates RowsHost with one ProviderRowView per
        // provider. The slot itself starts empty.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ProvidersPageLayout();
        await Assert.That(layout.RowsHost).IsNotNull();
        await Assert.That(layout.RowsHost.Children.Count).IsEqualTo(0);
        await Assert.That(layout.RowsHost).IsAssignableTo<StackPanel>();
    }
}

/// <summary>
/// <see cref="ProviderRowView"/>: declarative row layout (name + state +
/// 3-column input grid with masked TextBox, Reveal toggle, Save button).
/// The code-behind wires state + handlers from constructor args.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ProviderRowViewTests
{
    [Test]
    public async Task Root_IsUserControl_WrappingBorder()
    {
        AvaloniaTestHost.EnsureInitialized();
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        await Assert.That(row).IsAssignableTo<UserControl>();
        await Assert.That(row.Content).IsAssignableTo<Border>();
    }

    [Test]
    public async Task Border_HasRoundedCorners_AndCardChrome()
    {
        AvaloniaTestHost.EnsureInitialized();
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        var border = (Border)row.Content!;
        await Assert.That(border.CornerRadius).IsEqualTo(new CornerRadius(6));
        await Assert.That(border.Padding).IsEqualTo(new Thickness(12));
    }

    [Test]
    public async Task InputGrid_IsThreeColumns_KeyInputRevealSave()
    {
        AvaloniaTestHost.EnsureInitialized();
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        var border = (Border)row.Content!;
        var stack = (StackPanel)border.Child!;
        // StackPanel children: [NameLabel, StateLabel, inputGrid].
        var inputGrid = (Grid)stack.Children[2];
        await Assert.That(inputGrid.ColumnDefinitions.Count).IsEqualTo(3);
    }

    [Test]
    public async Task KeyInput_IsMaskedByDefault_WithPlaceholderAndMonospace()
    {
        AvaloniaTestHost.EnsureInitialized();
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        await Assert.That(row.KeyInput.PasswordChar).IsEqualTo('•');
        await Assert.That(row.KeyInput.RevealPassword).IsFalse();
        await Assert.That(row.KeyInput.PlaceholderText).IsEqualTo("Paste API key…");
        await Assert.That(row.KeyInput.FontFamily).IsEqualTo(AvaloniaTheme.MonoFontFamily);
    }

    [Test]
    public async Task SaveButton_IsFixedWidth()
    {
        AvaloniaTestHost.EnsureInitialized();
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        // Fixed width keeps the layout stable when the input changes width.
        await Assert.That(row.SaveButton.Width).IsEqualTo(80);
        await Assert.That(((string?)row.SaveButton.Content)).IsEqualTo("Save");
    }

    [Test]
    public async Task RevealToggle_IsAToggleButton()
    {
        AvaloniaTestHost.EnsureInitialized();
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        await Assert.That(row.RevealToggle).IsAssignableTo<ToggleButton>();
        await Assert.That(((string?)row.RevealToggle.Content)).IsEqualTo("Reveal");
        await Assert.That(row.RevealToggle.IsChecked).IsFalse();
    }

    /// <summary>
    /// In-memory <see cref="ICredentialStore"/> so the tests don't touch
    /// the user's real key file.
    /// </summary>
    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _keys = [];

        public string? Get(string name) => _keys.TryGetValue(name, out var v) ? v : null;
        public void Set(string name, string value) => _keys[name] = value;
        public void Delete(string name) => _keys.Remove(name);
        public bool Has(string name) => _keys.ContainsKey(name);
    }
}