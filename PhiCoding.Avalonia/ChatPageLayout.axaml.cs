using Avalonia.Controls;

namespace PhiCoding.Avalonia;

/// <summary>
/// Pure declarative layout for a single chat page: two slots — a star row
/// for the transcript, an auto row for the prompt input. The slots are
/// named <see cref="TranscriptHost"/> and <see cref="PromptInputHost"/>;
/// <see cref="ChatPageView"/> sets their <c>Content</c> to the live
/// transcript and prompt input controls at runtime.
/// </summary>
public partial class ChatPageLayout : UserControl
{
    public ChatPageLayout()
    {
        InitializeComponent();
    }
}