using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PhiCoding.Providers;

namespace PhiCoding.Avalonia.Components;

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

    public Control Root { get; }

    private ScrollViewer Build()
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
        };

        panel.Children.Add(new TextBlock { Text = "Providers", FontSize = 22, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "Connect an API key to use a provider with this session.",
            Foreground = AvaloniaTheme.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var entry in _providers.Providers)
        {
            panel.Children.Add(BuildProviderRow(entry));
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
    }

    private Border BuildProviderRow(ProviderCatalogEntry entry)
    {
        var isCurrent = entry.Name.Equals(_session.State.ProviderName, StringComparison.OrdinalIgnoreCase);
        var hasKey = _providers.HasApiKey(entry);

        var nameLabel = new TextBlock
        {
            Text = $"{entry.DisplayName} — {entry.Name}",
            FontWeight = FontWeight.SemiBold,
        };
        var stateLabel = new TextBlock
        {
            Text = isCurrent
                ? $"connected · {_session.State.Model}"
                : hasKey
                    ? "key saved"
                    : "not configured",
            Foreground = isCurrent ? AvaloniaTheme.Success : AvaloniaTheme.TextSecondary,
        };

        var connectButton = new Button
        {
            Content = isCurrent ? "Reconnect" : "Connect",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        connectButton.Click += async (_, _) => await PromptApiKey(entry);
        DockPanel.SetDock(connectButton, Dock.Right);

        var row = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = AvaloniaTheme.ContainerBackground,
            BorderBrush = AvaloniaTheme.ControlBorder,
            BorderThickness = new Thickness(1),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    connectButton,
                    new StackPanel
                    {
                        Spacing = 2,
                        Children = { nameLabel, stateLabel },
                    },
                },
            },
        };

        return row;
    }

    private async Task PromptApiKey(ProviderCatalogEntry entry)
    {
        if (_owner is null) return;

        var existingKey = _providers.ResolveApiKey(entry);
        var apiKeyInput = new TextBox
        {
            PlaceholderText = "API key",
            FontFamily = AvaloniaTheme.MonoFontFamily,
        };
        if (existingKey is { Length: > 0 })
            apiKeyInput.Text = existingKey;

        var hint = new TextBlock
        {
            Text = $"API key for {entry.DisplayName} ({entry.Name}) — Enter to connect, Esc to cancel.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = AvaloniaTheme.TextSecondary,
        };

        var connectButton = new Button { Content = "Connect", Width = 90 };
        var dialog = new Window
        {
            Title = $"Connect {entry.DisplayName}",
            Width = 420,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        DockPanel.SetDock(connectButton, Dock.Right);
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true,
            Children =
            {
                connectButton,
                new StackPanel
                {
                    Spacing = 8,
                    Children = { hint, apiKeyInput },
                },
            },
        };

        void Confirm()
        {
            var key = (apiKeyInput.Text ?? string.Empty).Trim();
            if (key.Length == 0) return;
            dialog.Close();
            ConnectWithKey(entry, key);
        }

        connectButton.Click += (_, _) => Confirm();
        dialog.KeyDown += (_, e) =>
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

        await dialog.ShowDialog(_owner);
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
