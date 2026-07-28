using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace PhiCoding.Tui;

/// <summary>
/// Multi-line prompt editor (tau's PromptInput equivalent, turn-based).
/// Enter submits; Shift+Enter (and any other key) falls through to
/// <see cref="TextView"/>'s default editing behavior (newline insertion).
/// </summary>
public sealed class PromptInput : TextView
{
    public event Action<string>? Submitted;

    protected override bool OnKeyDown(Key key)
    {
        if (key.KeyCode == KeyCode.Enter && !key.IsShift)
        {
            var text = (Text ?? "").Trim();
            Text = "";
            if (text.Length > 0) Submitted?.Invoke(text);
            return true;
        }
        return base.OnKeyDown(key);
    }
}
