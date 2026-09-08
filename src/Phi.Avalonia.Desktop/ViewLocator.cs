using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Phi.Avalonia.Desktop.Components.ChatLines;
using Phi.Avalonia.Desktop.ViewModels;
using Phi.Avalonia.Desktop.Views;

namespace Phi.Avalonia.Desktop;

/// <summary>
/// Strong-typed VM → View mapping. The original Avalonia template uses
/// reflection (<c>Type.GetType(name.Replace("ViewModel", "View"))</c> +
/// <c>Activator.CreateInstance(type)</c>), which NativeAOT can't trim —
/// both calls carry <c>[RequiresUnreferencedCode]</c>. This version is a
/// closed switch on the concrete VM types we ship today: adding a new
/// VM/View pair means adding one case here, no reflection involved.
/// Add a case BEFORE <c>_</c> when a new routed VM lands.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        return data switch
        {
            ShellViewModel => new ShellView(),

            // Top-level page switched by the sidebar (UI-A / Phase: page
            // routing). ShellView.xaml binds a ContentControl to
            // ShellViewModel.CurrentPage; this switch resolves it to the
            // right view.
            ChatPageViewModel => new ChatPageView(),
            ProvidersPageViewModel => new ProvidersPageView(),

            // Transcript chat-line types (UI-2). ItemsControl over
            // ChatPageViewModel.Transcript routes each item through this
            // switch without inline DataTemplates.
            UserTextLineViewModel => new UserTextLineView(),
            AssistantTextLineViewModel => new AssistantTextLineView(),
            ToolCardLineViewModel => new ToolCardLineView(),

            _ => new TextBlock
            {
                Text = $"ViewLocator: no view for {data?.GetType().Name ?? "<null>"}"
            }
        };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
