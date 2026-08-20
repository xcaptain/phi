using Avalonia.Controls;

namespace Phi.Avalonia;

/// <summary>
/// Pure declarative shell layout: left sidebar (New Chat + sessions list +
/// Models / Providers footer) and a right-side <see cref="ContentControl"/>
/// view host. The XAML in <c>ShellLayout.axaml</c> provides positions,
/// sizes, dividers and chrome styles; <see cref="ShellView"/> acts as the
/// controller — wiring events on the <c>x:Name</c>'d controls, dispatching
/// session navigation, and switching the view host between chat / Models /
/// Providers.
/// </summary>
public partial class ShellLayout : UserControl
{
    public ShellLayout()
    {
        InitializeComponent();
    }
}