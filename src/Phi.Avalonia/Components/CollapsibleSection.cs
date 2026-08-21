using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Phi.Avalonia.Components;

/// <summary>
/// A two-row layout: a clickable header row plus an optional detail body
/// that the header toggles. Used by thinking lines and tool cards so the
/// transcript defaults to a single-line summary and only expands the
/// detail on demand.
/// <para>
/// The header is a transparent <see cref="Border"/> wrapping a
/// <see cref="DockPanel"/> (title on the left, chevron on the right); the
/// body is a <see cref="ContentControl"/> hidden when collapsed. We use
/// <see cref="InputElement.PointerPressed"/> on the border instead of a
/// <see cref="Button"/>'s Click event because transparent + 0-border
/// Buttons in Avalonia 12 have flaky hit-testing — only the chevron area
/// reliably receives clicks, the title text falls through to the parent.
/// A Border with hit-test on its full bounds is the stable equivalent.
/// </para>
/// <para>
/// The whole section has no background or border so it sits flush against
/// the ScrollViewer — visual contrast is the chevron rotation only.
/// </para>
/// </summary>
public sealed class CollapsibleSection : UserControl
{
    private readonly ContentControl _bodyHost = new();
    private readonly Border _headerArea;
    private readonly MaterialIcon _chevron;
    private bool _isExpanded;
    private Func<Control>? _lazyBodyFactory;

    /// <summary>Builds a section with the given header / body, collapsed by default.</summary>
    /// <param name="headerContent">Always-visible header row (title text + icon, etc.).</param>
    /// <param name="bodyContent">Detail body shown when expanded.</param>
    /// <param name="startExpanded">Initial expansion state.</param>
    public CollapsibleSection(Control headerContent, Control bodyContent, bool startExpanded = false)
    {
        ArgumentNullException.ThrowIfNull(headerContent);
        ArgumentNullException.ThrowIfNull(bodyContent);

        _isExpanded = startExpanded;

        _chevron = new MaterialIcon
        {
            Kind = startExpanded ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight,
            Width = 14,
            Height = 14,
            Foreground = AvaloniaTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var headerRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_chevron, Dock.Right);
        headerRow.Children.Add(_chevron);
        headerRow.Children.Add(headerContent);

        _headerArea = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = headerRow,
        };
        _headerArea.PointerPressed += OnHeaderPressed;

        _bodyHost.Content = bodyContent;
        _bodyHost.IsVisible = startExpanded;

        var root = new StackPanel { Spacing = 0 };
        root.Children.Add(_headerArea);
        root.Children.Add(_bodyHost);
        Content = root;
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        // Left-button only — right-click context menus / middle-clicks
        // shouldn't toggle the section.
        if (!e.GetCurrentPoint(_headerArea).Properties.IsLeftButtonPressed) return;
        IsExpanded = !_isExpanded;
        e.Handled = true;
    }

    /// <summary>Expansion state. Setting flips the body visibility + chevron.
    /// Expanding also builds a pending lazy body (see
    /// <see cref="SetLazyBody"/>) so expensive detail content is deferred
    /// until the user actually opens the section.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (value)
                BuildLazyBody();
            _bodyHost.IsVisible = value;
            _chevron.Kind = value ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;
        }
    }

    /// <summary>Swaps the body content. Useful for tool cards that replace the
    /// "…" placeholder with the real result on completion.</summary>
    public void SetBody(Control body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _lazyBodyFactory = null;
        _bodyHost.Content = body;
    }

    /// <summary>
    /// Defers body construction until the section is first expanded. The
    /// factory runs once, on expand, and the result is cached — collapsing
    /// and re-expanding reuses it. Used for expensive tool-card detail
    /// bodies (e.g. the read card's syntax-highlighted file content) so a
    /// long transcript doesn't build hundreds of collapsed bodies up front.
    /// </summary>
    public void SetLazyBody(Func<Control> bodyFactory)
    {
        ArgumentNullException.ThrowIfNull(bodyFactory);
        _lazyBodyFactory = bodyFactory;
        if (_isExpanded)
            BuildLazyBody();
    }

    private void BuildLazyBody()
    {
        if (_lazyBodyFactory is null) return;
        var factory = _lazyBodyFactory;
        _lazyBodyFactory = null;
        _bodyHost.Content = factory();
    }

    /// <summary>The header title control (tests / external updates).</summary>
    public Control HeaderContent
    {
        get
        {
            var root = (StackPanel)Content!;
            var headerArea = (Border)root.Children[0];
            var headerRow = (DockPanel)headerArea.Child!;
            return (Control)headerRow.Children[1];
        }
    }

    /// <summary>The body control (tests).</summary>
    public Control BodyContent => (Control)_bodyHost.Content!;
}
