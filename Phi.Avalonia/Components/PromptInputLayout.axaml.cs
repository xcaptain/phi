using Avalonia.Controls;

namespace Phi.Avalonia.Components;

/// <summary>
/// Pure declarative chrome for the prompt input: a single rounded
/// <see cref="Border"/> hosting the multi-line editor and a bottom
/// toolbar (model picker / workspace picker / submit button). Named
/// controls are wired by <see cref="PromptInputView"/> at runtime.
/// </summary>
public partial class PromptInputLayout : UserControl
{
    public PromptInputLayout()
    {
        InitializeComponent();
    }
}
