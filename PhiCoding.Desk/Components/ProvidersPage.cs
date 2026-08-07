using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiCoding.Providers;

namespace PhiCoding.Desk.Components;

/// <summary>
/// The Providers settings page. Lists the built-in providers with their
/// connection state (has API key / current model); each row has a
/// Connect button that opens a modal dialog to enter (or replace) the API
/// key. On confirm the provider is created and the live session switches
/// to it with the provider's default model.
/// </summary>
public sealed class ProvidersPage
{
    private readonly ISession _session;
    private readonly ProviderManager _providers;
    private readonly Window? _owner;

    public ProvidersPage(ISession session, ProviderManager providers, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(providers);
        _session = session;
        _providers = providers;
        _owner = owner;
        Root = Build();
    }

    public FrameworkElement Root { get; }

    private ScrollViewer Build()
    {
        var children = new List<FrameworkElement>
        {
            new Label().Text("Providers").FontSize(22).Bold(),
            new Label()
                .Text("Connect an API key to use a provider with this session.")
                .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)))
                .TextWrapping(TextWrapping.Wrap),
        };

        foreach (var entry in _providers.Providers)
        {
            children.Add(BuildProviderRow(entry));
        }

        return new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(24)
            .Content(new StackPanel()
                .Orientation(Aprillz.MewUI.Orientation.Vertical)
                .Spacing(12)
                .Children(children.ToArray()));
    }

    private Border BuildProviderRow(ProviderCatalogEntry entry)
    {
        var isCurrent = entry.Name.Equals(_session.State.ProviderName, StringComparison.OrdinalIgnoreCase);
        var hasKey = _providers.HasApiKey(entry);

        var nameLabel = new Label()
            .Text($"{entry.DisplayName} — {entry.Name}")
            .SemiBold();
        var stateLabel = new Label()
            .Text(isCurrent
                ? $"connected · {_session.State.Model}"
                : hasKey
                    ? "key saved"
                    : "not configured")
            .WithTheme((t, c) => c.Foreground(
                isCurrent ? DeskTheme.Success(t) : DeskTheme.TextSecondary(t)));

        var connectButton = new Button()
            .Content(isCurrent ? "Reconnect" : "Connect")
            .Width(90)
            .OnClick(() => PromptApiKey(entry));

        var row = new Border()
            .Padding(12)
            .CornerRadius(6)
            .WithTheme((t, b) =>
            {
                b.Background(t.Palette.ContainerBackground);
                b.BorderBrush(t.Palette.ControlBorder);
            })
            .BorderThickness(1)
            .Child(
                new DockPanel()
                    .LastChildFill()
                    .Children(
                        connectButton.DockRight(),
                        new StackPanel()
                            .Orientation(Aprillz.MewUI.Orientation.Vertical)
                            .Spacing(2)
                            .Children(nameLabel, stateLabel)));

        return row;
    }

    private async void PromptApiKey(ProviderCatalogEntry entry)
    {
        var existingKey = _providers.ResolveApiKey(entry);
        var apiKeyInput = new TextBox()
            .Placeholder("API key")
            .FontFamily("Consolas");
        if (existingKey is { Length: > 0 })
            apiKeyInput.Text = existingKey;

        var hint = new Label()
            .Text($"API key for {entry.DisplayName} ({entry.Name}) — Enter to connect, Esc to cancel.")
            .TextWrapping(TextWrapping.Wrap)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));

        var dialog = new Window()
            .Title($"Connect {entry.DisplayName}")
            .Padding(16);
        dialog.WindowSize = WindowSize.Fixed(420, 160);

        var connectButton = new Button()
            .Content("Connect")
            .Width(90)
            .OnClick(() => Confirm());
        dialog.Content = new DockPanel()
            .LastChildFill()
            .Children(
                connectButton.DockRight(),
                new StackPanel()
                    .Orientation(Aprillz.MewUI.Orientation.Vertical)
                    .Spacing(8)
                    .Children(hint, apiKeyInput));

        void Confirm()
        {
            var key = (apiKeyInput.Text ?? string.Empty).Trim();
            if (key.Length == 0) return;
            dialog.Close();
            ConnectWithKey(entry, key);
        }

        dialog.PreviewKeyDown += e =>
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        await dialog.ShowDialogAsync(_owner);
    }

    private void ConnectWithKey(ProviderCatalogEntry entry, string apiKey)
    {
        _providers.SetApiKey(entry, apiKey);
        var provider = _providers.CreateProvider(entry, apiKey);
        var model = _providers.ResolveDefaultModel(entry);
        _session.SwitchProvider(provider, entry.Name, model);
        _providers.SaveDefault(entry, model);
    }
}
