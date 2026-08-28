using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Phi.Avalonia.Desktop2.Components;

/// <summary>
/// Prompt input layout container. Pure layout / x:Name wiring — the
/// picker data and behaviour live on <see cref="ViewModels.PromptInputViewModel"/>
/// (model picker filter, workspace sentinel, send command). The slash
/// auto-complete popup that the old <c>Phi.Avalonia.Components.PromptInputView</c>
/// wired stays pending in this prototype; the <c>AutoCompleteRoot</c>
/// Border is part of the layout so a follow-up sprint can light it up
/// without touching surrounding geometry.
/// </summary>
public partial class PromptInputView : UserControl
{
    public PromptInputView()
    {
        InitializeComponent();
    }
}