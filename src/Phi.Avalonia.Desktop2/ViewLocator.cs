using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Phi.Avalonia.Desktop2.ViewModels;
using Phi.Avalonia.Desktop2.Views;

namespace Phi.Avalonia.Desktop2;

/// <summary>
/// Strong-typed VM → View mapping. The original Avalonia template uses
/// reflection (<c>Type.GetType(name.Replace("ViewModel", "View"))</c> +
/// <c>Activator.CreateInstance(type)</c>), which NativeAOT can't trim —
/// both calls carry <c>[RequiresUnreferencedCode]</c>. This version is a
/// closed switch on the concrete VM types we ship today: adding a new
/// VM/View pair means adding one case here, no reflection involved.
/// Add a case BEFORE <c>_</c> when a new top-level routed VM lands
/// (Phase UI-2+ — ProvidersPageViewModel, ...).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        return data switch
        {
            ShellViewModel => new ShellView(),
            _ => new TextBlock
            {
                Text = $"ViewLocator: no view for {data?.GetType().Name ?? "<null>"}"
            }
        };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
