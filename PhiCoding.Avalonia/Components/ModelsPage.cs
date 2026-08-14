using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using PhiCoding.Providers;

namespace PhiCoding.Avalonia.Components;

/// <summary>
/// The Models settings page. Shows the active provider + model and a
/// picker for the provider's available models; selecting a model switches
/// the live session via <see cref="ISession.SwitchModel"/>. Built fresh on
/// each visit so it always reflects the current session's provider.
/// </summary>
public sealed class ModelsPage
{
    private readonly ISession _session;
    private readonly ProviderManager _providers;

    public ModelsPage(ISession session, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(providers);
        _session = session;
        _providers = providers;
        Root = Build();
    }

    public Control Root { get; }

    /// <summary>The built model picker, exposed for structural tests.</summary>
    internal ListBox? ModelsList { get; private set; }

    private ScrollViewer Build()
    {
        var provider = _session.State.ProviderName;
        var model = _session.State.Model;

        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
        };

        panel.Children.Add(new TextBlock { Text = "Models", FontSize = 22, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = provider.Length > 0 ? $"{provider} · {model}" : "Not connected",
            Foreground = AvaloniaTheme.TextSecondary,
        });

        var entry = _providers.Providers.FirstOrDefault(
            p => p.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));

        if (entry is null || entry.Models.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No provider connected. Open Providers to connect an API key.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Provider models",
                FontWeight = FontWeight.SemiBold,
            });

            var list = new ListBox
            {
                MaxHeight = 260,
                ItemsSource = entry.Models,
            };
            var index = entry.Models.ToList().IndexOf(model);
            if (index >= 0) list.SelectedIndex = index;
            ModelsList = list;

            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is string m && m != _session.State.Model)
                    _session.SwitchModel(m);
            };

            panel.Children.Add(list);
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
    }
}
