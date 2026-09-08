using Phi.Avalonia.Desktop.ViewModels;

namespace Phi.Avalonia.Desktop.Components.ChatLines;

/// <summary>
/// One row of user-typed text. Renders as a right-aligned accent bubble
/// (<see cref="UserTextLineView"/>). The transcript projector supplies the
/// text; UI-2 ships with a 5-line mock so the layout is reviewable.
/// </summary>
public sealed class UserTextLineViewModel : ViewModelBase
{
    public string Text { get; }

    public UserTextLineViewModel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }
}
