using Phi.Avalonia.Desktop2.ViewModels;

namespace Phi.Avalonia.Desktop2.Components.ChatLines;

/// <summary>
/// One block of assistant markdown. <see cref="Markdown"/> is the raw
/// markdown source — the view renders it via
/// <c>MarkView.Avalonia.MarkdownViewer</c>, which handles headings / code /
/// lists / inline formatting.
/// </summary>
public sealed class AssistantTextLineViewModel : ViewModelBase
{
    public string Markdown { get; }

    public AssistantTextLineViewModel(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        Markdown = markdown;
    }
}
