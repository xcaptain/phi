using Terminal.Gui.Drivers;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace PhiCoding.Tui;

/// <summary>
/// Multi-line prompt editor (tau's PromptInput equivalent, turn-based).
///
/// Built on <see cref="Editor"/> (the tui-cs/Editor View that supersedes
/// <c>TextView</c>: rope-backed document model, undo/redo, multi-caret,
/// syntax highlighting, folding, find/replace, soft wrap).
///
/// Key handling:
/// <list type="bullet">
/// <item><b>Enter</b> submits via <see cref="Command.Accept"/>.</item>
/// <item><b>Shift+Enter / Cmd+Enter / Ctrl+Enter</b> insert a newline via
/// <see cref="Command.NewLine"/>.</item>
/// </list>
///
/// Cross-platform note: most terminals cannot distinguish Shift+Enter from
/// plain Enter (the underlying ANSI byte is identical — CR/LF only).
/// Terminal.Gui's driver only reports the Shift modifier when the terminal
/// supports the Kitty keyboard protocol (Ghostty, recent Kitty/iTerm2,
/// Windows Terminal). On macOS, <b>Cmd+Enter</b> reliably produces a
/// distinct modifier sequence and is the de-facto standard there; the v2
/// driver surfaces it as <see cref="Key.IsAlt"/>. We therefore treat any
/// modified Enter (Shift, Alt, or Ctrl) as a newline trigger.
/// </summary>
public sealed class PromptInput : Editor
{
    public event Action<string>? Submitted;

    public PromptInput()
    {
        Multiline = true;
        WordWrap = false;
        ReadOnly = false;

        // Strip the default Editor binding: Key.Enter → Command.NewLine.
        KeyBindings.Remove(Key.Enter);

        // Plain Enter → Accept (the v2 standard submit pipeline). The
        // Accepted event below is what actually triggers the submit handler.
        // Modified Enter variants cannot be added as KeyBindings because
        // Key equality compares KeyCode only — they would alias plain Enter
        // and either be stripped by Remove(Key.Enter) or trigger both
        // NewLine and Accept simultaneously. They are intercepted in
        // OnKeyDown below.
        KeyBindings.Add(Key.Enter, Command.Accept);

        Accepted += OnAccepted;
    }

    protected override bool OnKeyDown(Key key)
    {
        // Any modified Enter → newline. Covers:
        //   - Shift+Enter (modern terminals with Kitty keyboard protocol)
        //   - Cmd+Enter on macOS (driver reports as IsAlt)
        //   - Ctrl+Enter as a portable fallback (works everywhere)
        // Plain Enter (no modifier) falls through to base.OnKeyDown and
        // then to the Accept binding above.
        if (key.KeyCode == KeyCode.Enter && (key.IsShift || key.IsAlt || key.IsCtrl))
        {
            InvokeCommand(Command.NewLine);
            return true;
        }
        return base.OnKeyDown(key);
    }

    private void OnAccepted(object? sender, CommandEventArgs e)
    {
        var text = (Text ?? "").Trim();
        Text = "";
        if (text.Length > 0) Submitted?.Invoke(text);
    }
}