using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhiCoding.Avalonia.Components;
using PhiCoding.Providers;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ProvidersPage"/>: each built-in provider gets a row with an
/// inline masked <see cref="TextBox"/>, a Reveal toggle, and a Save
/// button. Saving just persists the key to <see cref="ProviderManager"/>;
/// activating the provider on the live session is the prompt input's
/// model picker's job.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ProvidersPageTests
{
    private static (ProviderManager providers, StackPanel rowsHost) CreatePage(Action<ProviderManager>? setup = null)
    {
        AvaloniaTestHost.EnsureInitialized();
        // Inject a hermetic in-memory credential store so the tests don't
        // touch the user's real key file and start from a clean slate.
        // setup() runs BEFORE the page is built, because rows read
        // HasApiKey / ResolveApiKey at construction time.
        var providers = new ProviderManager(credentials: new FakeCredentialStore());
        setup?.Invoke(providers);
        var page = new ProvidersPage(providers);
        // page.Root is now ProvidersPageLayout (UserControl); walk into the
        // chrome: UserControl.Content -> ScrollViewer -> StackPanel ->
        // [title, hint, RowsHost].
        var layout = page.Root;
        var scrollViewer = (ScrollViewer)((ContentControl)layout).Content!;
        var outerPanel = (StackPanel)scrollViewer.Content!;
        var rowsHost = (StackPanel)outerPanel.Children[2];
        return (providers, rowsHost);
    }

    private sealed record RowParts(ProviderRowView Row, TextBlock NameLabel, TextBlock StateLabel,
        TextBox KeyInput, ToggleButton RevealToggle, Button SaveButton);

    private static RowParts FirstRow(StackPanel rowsHost)
    {
        var row = (ProviderRowView)rowsHost.Children[0];
        // ProviderRowView exposes its named controls as properties.
        return new RowParts(row, row.NameLabel, row.StateLabel, row.KeyInput, row.RevealToggle, row.SaveButton);
    }

    [Test]
    public async Task ListsARowPerBuiltInProvider()
    {
        var (providers, rowsHost) = CreatePage();
        await Assert.That(rowsHost.Children.Count).IsEqualTo(providers.Providers.Count);
    }

    [Test]
    public async Task Header_HasTitleAndHint()
    {
        var (_, rowsHost) = CreatePage();
        // Walk back to the outer StackPanel to check the header.
        var outerPanel = (StackPanel)rowsHost.Parent!;
        await Assert.That(((TextBlock)outerPanel.Children[0]).Text).IsEqualTo("Providers");
        await Assert.That(((TextBlock)outerPanel.Children[1]).Text).Contains("API key");
    }

    [Test]
    public async Task NewRow_StartsAsNotConfigured()
    {
        var (providers, panel) = CreatePage();
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        await Assert.That(parts.StateLabel.Text).IsEqualTo("not configured");
        await Assert.That(parts.StateLabel.Foreground).IsEqualTo(AvaloniaTheme.TextSecondary);
        await Assert.That(parts.KeyInput.Text).IsEqualTo("");
    }

    [Test]
    public async Task ExistingKey_ShowsAsKeySavedAndIsMasked()
    {
        var (providers, panel) = CreatePage(p => p.SetApiKey(p.Providers[0], "saved-key-for-test"));
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        await Assert.That(parts.StateLabel.Text).IsEqualTo("✓ key saved");
        await Assert.That(parts.StateLabel.Foreground).IsEqualTo(AvaloniaTheme.Success);
        // The TextBox is masked by default — the saved key is loaded but
        // the user sees only the mask character (PasswordChar).
        await Assert.That(parts.KeyInput.PasswordChar).IsEqualTo('•');
        await Assert.That(parts.KeyInput.RevealPassword).IsFalse();
    }

    [Test]
    public async Task ClickingSave_PersistsKeyToProviderManager()
    {
        var (providers, panel) = CreatePage();
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        parts.KeyInput.Text = "fresh-api-key-12345";
        parts.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Assert.That(providers.HasApiKey(firstEntry)).IsTrue();
        await Assert.That(providers.ResolveApiKey(firstEntry)).IsEqualTo("fresh-api-key-12345");
    }

    [Test]
    public async Task ClickingSave_UpdatesRowStateToKeySaved()
    {
        var (providers, panel) = CreatePage();
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        parts.KeyInput.Text = "another-key";
        parts.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Assert.That(parts.StateLabel.Text).IsEqualTo("✓ key saved");
        await Assert.That(parts.StateLabel.Foreground).IsEqualTo(AvaloniaTheme.Success);
    }

    [Test]
    public async Task EnterOnKeyInput_PersistsAndUpdatesState()
    {
        var (providers, panel) = CreatePage();
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        parts.KeyInput.Text = "via-enter";
        parts.KeyInput.RaiseEvent(new KeyEventArgs
        {
            Key = Key.Enter,
            RoutedEvent = InputElement.KeyDownEvent,
        });

        await Assert.That(providers.ResolveApiKey(firstEntry)).IsEqualTo("via-enter");
        await Assert.That(parts.StateLabel.Text).IsEqualTo("✓ key saved");
    }

    [Test]
    public async Task SaveWithEmptyKey_DoesNotPersist()
    {
        var (providers, panel) = CreatePage();
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        // Don't change KeyInput.Text — leave it empty.
        parts.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Assert.That(providers.HasApiKey(firstEntry)).IsFalse();
        await Assert.That(parts.StateLabel.Text).IsEqualTo("not configured");
    }

    [Test]
    public async Task Save_TrimsWhitespace()
    {
        var (providers, panel) = CreatePage();
        var firstEntry = providers.Providers[0];

        var parts = FirstRow(panel);
        parts.KeyInput.Text = "   key-with-spaces   ";
        parts.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Assert.That(providers.ResolveApiKey(firstEntry)).IsEqualTo("key-with-spaces");
    }

    [Test]
    public async Task RevealToggle_FlipsKeyInputRevealPassword()
    {
        var (_, panel) = CreatePage();
        var parts = FirstRow(panel);

        await Assert.That(parts.KeyInput.RevealPassword).IsFalse();
        parts.RevealToggle.IsChecked = true;
        await Assert.That(parts.KeyInput.RevealPassword).IsTrue();
        parts.RevealToggle.IsChecked = false;
        await Assert.That(parts.KeyInput.RevealPassword).IsFalse();
    }

    [Test]
    public async Task KeyInput_UsesMonospaceFont()
    {
        // API keys are easier to read / paste with a monospace font.
        var (_, panel) = CreatePage();
        var parts = FirstRow(panel);
        await Assert.That(parts.KeyInput.FontFamily).IsEqualTo(AvaloniaTheme.MonoFontFamily);
    }

    [Test]
    public async Task InputRow_IsThreeColumns_KeyFillRevealSave()
    {
        var (_, panel) = CreatePage();
        var parts = FirstRow(panel);
        // ProviderRowView: Border > StackPanel [name, state, inputGrid].
        var border = (Border)parts.Row.Content!;
        var stack = (StackPanel)border.Child!;
        var inputGrid = (Grid)stack.Children[2];
        await Assert.That(inputGrid.ColumnDefinitions.Count).IsEqualTo(3);
        // Save button has a fixed width so the layout doesn't reflow when
        // the input changes width.
        await Assert.That(parts.SaveButton.Width).IsEqualTo(80);
    }

    /// <summary>
    /// In-memory <see cref="ICredentialStore"/> so the tests don't touch
    /// the user's real key file and start from a clean slate.
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