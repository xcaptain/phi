using Avalonia.Controls;
using Phi.Providers;

namespace Phi.Avalonia.Components;

/// <summary>
/// The Providers settings page. Composes one
/// <see cref="ProviderRowView"/> per built-in provider into the
/// <see cref="ProvidersPageLayout.RowsHost"/> slot. The page owns no UI
/// state itself — every row reads its entry from the catalog and its
/// credentials from the shared <see cref="ProviderManager"/>.
/// </summary>
public sealed class ProvidersPage
{
    private readonly ProvidersPageLayout _layout;

    public ProvidersPage(ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _layout = new ProvidersPageLayout();
        foreach (var entry in providers.Providers)
            _layout.RowsHost.Children.Add(new ProviderRowView(entry, providers));
    }

    public Control Root => _layout;
}
