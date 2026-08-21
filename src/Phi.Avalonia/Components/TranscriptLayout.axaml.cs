using Avalonia.Controls;

namespace Phi.Avalonia.Components;

/// <summary>
/// Pure declarative chrome for the chat transcript: a scrolling view
/// with document-style reading margins around a named <see cref="LinesPanel"/>
/// slot. <see cref="TranscriptView"/> fills the slot with one Control per
/// <see cref="Phi.Chat.ChatLine"/>, DIFFed by stable Id.
/// </summary>
public partial class TranscriptLayout : UserControl
{
    public TranscriptLayout()
    {
        InitializeComponent();
    }
}
