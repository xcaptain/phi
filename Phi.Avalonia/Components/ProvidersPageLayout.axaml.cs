using Avalonia.Controls;

namespace Phi.Avalonia.Components;

/// <summary>
/// Pure declarative chrome for the Providers settings page: a header,
/// a one-line description, and a named <see cref="RowsHost"/> slot for
/// the per-provider rows. <see cref="ProvidersPage"/> populates the
/// slot with one <see cref="ProviderRowView"/> per built-in provider.
/// </summary>
public partial class ProvidersPageLayout : UserControl
{
    public ProvidersPageLayout()
    {
        InitializeComponent();
    }
}
