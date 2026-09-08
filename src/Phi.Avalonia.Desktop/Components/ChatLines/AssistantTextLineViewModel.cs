using CommunityToolkit.Mvvm.ComponentModel;
using Phi.Avalonia.Desktop.ViewModels;

namespace Phi.Avalonia.Desktop.Components.ChatLines;

/// <summary>
/// One block of assistant markdown. <see cref="Markdown"/> is the raw
/// markdown source — the view renders it via
/// <c>MarkView.Avalonia.MarkdownViewer</c>, which handles headings / code /
/// lists / inline formatting.
/// <para>
/// <see cref="Markdown"/> is an <c>[ObservableProperty]</c> so the view
/// re-renders on streaming token updates without rebuilding the line
/// VM. <see cref="Phi.Avalonia.Desktop.ViewModels.ChatPageViewModel"/>
/// patches this property in place when the harness streams a new
/// chunk of assistant text into the same message slot, instead of
/// clearing the whole transcript and rebuilding every line on every
/// <c>StateChanged</c>.
/// </para>
/// </summary>
public sealed partial class AssistantTextLineViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _markdown = "";

    public AssistantTextLineViewModel(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }
}
