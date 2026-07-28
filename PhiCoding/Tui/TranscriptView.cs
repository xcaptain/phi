using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace PhiCoding.Tui;

/// <summary>
/// Scrollable, colored transcript view (tau's TranscriptView equivalent).
/// Rebuilds wrapped lines from <see cref="TuiState.Items"/> on every change
/// (cheap: wrapping is plain string slicing). Follows the bottom while
/// <c>_scrollBack == 0</c>; any scroll-up breaks follow mode, scrolling back
/// to the bottom re-enables it.
/// </summary>
public sealed class TranscriptView : View
{
    private readonly TuiState _state;
    private List<TranscriptLine> _wrapped = [];
    private int _wrappedWidth = -1;
    private int _scrollBack;

    public TranscriptView(TuiState state)
    {
        _state = state;
        _state.Changed += OnStateChanged;
        CanFocus = true;

        MouseEvent += (_, me) =>
        {
            if (me.Flags.HasFlag(MouseFlags.WheeledUp)) { ScrollBy(3); me.Handled = true; }
            else if (me.Flags.HasFlag(MouseFlags.WheeledDown)) { ScrollBy(-3); me.Handled = true; }
        };
    }

    private void OnStateChanged()
    {
        Rebuild();
        SetNeedsDraw();
    }

    private void Rebuild()
    {
        var width = Math.Max(1, Viewport.Width);
        _wrapped = TranscriptWrapper.Wrap(_state.Items, width);
        _wrappedWidth = width;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (Viewport.Width != _wrappedWidth) Rebuild();

        var height = Viewport.Height;
        var maxScrollBack = Math.Max(0, _wrapped.Count - height);
        _scrollBack = Math.Clamp(_scrollBack, 0, maxScrollBack);
        var top = maxScrollBack - _scrollBack;

        for (var row = 0; row < height && top + row < _wrapped.Count; row++)
        {
            var line = _wrapped[top + row];
            Move(0, row);
            SetAttribute(AttributeFor(line.Style));
            AddStr(line.Text);
        }
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        var page = Math.Max(1, Viewport.Height - 1);
        switch (key.KeyCode)
        {
            case KeyCode.CursorUp: ScrollBy(1); return true;
            case KeyCode.CursorDown: ScrollBy(-1); return true;
            case KeyCode.PageUp: ScrollBy(page); return true;
            case KeyCode.PageDown: ScrollBy(-page); return true;
            case KeyCode.Home: ScrollBy(int.MaxValue / 2); return true;
            case KeyCode.End: ScrollBy(int.MinValue / 2); return true;
        }
        return base.OnKeyDown(key);
    }

    private void ScrollBy(int delta)
    {
        _scrollBack += delta;
        SetNeedsDraw();
    }

    private Terminal.Gui.Drawing.Attribute AttributeFor(TranscriptStyle style)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);
        var bg = normal.Background;
        return style switch
        {
            TranscriptStyle.User => new Terminal.Gui.Drawing.Attribute(new Color("BrightCyan"), bg, TextStyle.Bold),
            TranscriptStyle.ToolCall => new Terminal.Gui.Drawing.Attribute(new Color("Cyan"), bg),
            TranscriptStyle.ToolOk => new Terminal.Gui.Drawing.Attribute(new Color("Green"), bg),
            TranscriptStyle.ToolError => new Terminal.Gui.Drawing.Attribute(new Color("Red"), bg, TextStyle.Bold),
            TranscriptStyle.ToolOutput => new Terminal.Gui.Drawing.Attribute(new Color("BrightBlack"), bg),
            TranscriptStyle.DiffAdded => new Terminal.Gui.Drawing.Attribute(new Color("Green"), bg),
            TranscriptStyle.DiffRemoved => new Terminal.Gui.Drawing.Attribute(new Color("Red"), bg),
            TranscriptStyle.DiffMeta => new Terminal.Gui.Drawing.Attribute(new Color("BrightBlack"), bg),
            TranscriptStyle.Status => new Terminal.Gui.Drawing.Attribute(new Color("BrightBlack"), bg, TextStyle.Italic),
            TranscriptStyle.Error => new Terminal.Gui.Drawing.Attribute(new Color("BrightRed"), bg, TextStyle.Bold),
            _ => normal,
        };
    }
}
