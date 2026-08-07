using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiCoding.Providers;

namespace PhiCoding.Desk.Components;

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

    public FrameworkElement Root { get; }

    /// <summary>The built model picker, exposed for structural tests.</summary>
    internal ListBox? ModelsList { get; private set; }

    private ScrollViewer Build()
    {
        var provider = _session.State.ProviderName;
        var model = _session.State.Model;

        var children = new List<FrameworkElement>
        {
            new Label().Text("Models").FontSize(22).Bold(),
            new Label()
                .Text(provider.Length > 0 ? $"{provider} · {model}" : "Not connected")
                .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t))),
        };

        var entry = _providers.Providers.FirstOrDefault(
            p => p.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));

        if (entry is null || entry.Models.Count == 0)
        {
            children.Add(new Label()
                .Text("No provider connected. Open Providers to connect an API key.")
                .TextWrapping(TextWrapping.Wrap));
        }
        else
        {
            children.Add(new Label().Text("Provider models").Bold());

            var models = entry.Models;
            var list = new ListBox()
                .Height(260)
                .Items(models.ToArray());
            var index = models.ToList().IndexOf(model);
            if (index >= 0) list.SelectedIndex = index;
            ModelsList = list;

            list.SelectionChanged += selected =>
            {
                if (selected is string m && m != _session.State.Model)
                    _session.SwitchModel(m);
            };

            children.Add(list);
        }

        return new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(24)
            .Content(new StackPanel()
                .Orientation(Aprillz.MewUI.Orientation.Vertical)
                .Spacing(12)
                .Children(children.ToArray()));
    }
}