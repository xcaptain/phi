using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace PhiCoding.Tui;

/// <summary>
/// Wraps the TUI root and auto-copies the active <see cref="ISelectionOwner"/>
/// selection to the system clipboard the moment a mouse drag-select or
/// double-click word-select completes. This sidesteps the macOS terminal
/// emulator, which intercepts <c>Cmd+C</c> and never delivers it to the TUI
/// process, by making the copy implicit in the act of selecting.
/// <para>
/// The host hooks the bubbling <see cref="Visual.PointerReleasedEvent"/> with
/// <c>handledEventsToo: true</c> because <see cref="Paragraph"/>,
/// <see cref="TextBlock"/>, and <see cref="Markup"/> all mark the release as
/// handled once a drag-select finishes — a plain handledEventsToo hook is the
/// only way to observe those releases from above.
/// </para>
/// <para>
/// Pointer events in this library carry <see cref="RoutingStrategy.Preview"/>
/// <i>and</i> <see cref="RoutingStrategy.Bubble"/>, so the same release
/// visits this handler twice (once top-down, once bottom-up). We ignore
/// the preview pass via <see cref="RoutingPhase"/> — only the bubble
/// pass fires after the inner control has finalized its selection, and
/// only the bubble pass sees a non-zero <see cref="RoutedEventArgs.Handled"/>
/// flag for drag-selects.
/// </para>
/// </summary>
public sealed class SelectionCopyHost : ContentVisual
{
    /// <summary>
    /// Finds the nearest selectable <see cref="ISelectionOwner"/> ancestor of
    /// <paramref name="source"/>, walking up the parent chain. Exposed as a
    /// static helper so the policy can be unit-tested without raising real
    /// routed events.
    /// </summary>
    public static bool TryFindSelectableOwner(Visual? source, out ISelectionOwner owner)
    {
        for (var v = source; v is not null; v = v.Parent)
        {
            if (v is ISelectionOwner candidate && candidate.IsSelectable)
            {
                owner = candidate;
                return true;
            }
        }

        owner = null!;
        return false;
    }

    /// <summary>
    /// Wraps <paramref name="content"/>. The host must be the root visual
    /// passed to <c>Terminal.Run</c> (or wrapped further by a sibling
    /// <see cref="ContentVisual"/> such as <c>ToastHost</c>) so it sits in the
    /// bubble chain for every pointer release in the UI.
    /// </summary>
    public SelectionCopyHost(Visual content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        // Stretch in both axes: <see cref="ContentVisual.MeasureCore"/>
        // delegates measure to the inner content, but the wrapper itself
        // must claim the full viewport so the layout system arranges the
        // DockLayout root to the entire screen. Without this the wrapped
        // tree collapses to its natural size and the transcript area
        // disappears.
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
        // Don't steal focus from the prompt editor that lives inside.
        Focusable = false;
        // handledEventsToo is required: Paragraph/TextBlock/Markup all mark
        // PointerReleased as handled when a drag-select finishes, so a plain
        // handler on the root wouldn't see those events.
        AddHandler(PointerReleasedEvent, OnPointerReleasedHandledToo, handledEventsToo: true);
    }

    private void OnPointerReleasedHandledToo(object? sender, PointerEventArgs e)
    {
        // PointerReleased is registered with both Preview and Bubble routing
        // strategies, so the same release visits this handler twice (once
        // top-down, once bottom-up). The Preview pass happens before the
        // inner control finalizes its selection, so we explicitly skip it.
        if (e.RoutingPhase != RoutingPhase.Bubble)
        {
            return;
        }

        if (e.Button != TerminalMouseButton.Left)
        {
            return;
        }

        if (!TryFindSelectableOwner(e.OriginalSource, out var owner))
        {
            return;
        }

        if (!owner.HasSelection || !owner.TryCopySelection(out var text) || text.Length == 0)
        {
            return;
        }

        // Shell out to the OS clipboard (pbcopy / clip / xclip) instead of
        // going through the terminal emulator's clipboard, which most
        // macOS terminals never forward to the host. SystemClipboard
        // swallows errors; on the rare machine with no helper installed we
        // silently skip the toast rather than mislead the user.
        if (SystemClipboard.TrySetText(text))
        {
            ShowCopiedToast($"Copied {text.Length} chars");
        }

        // Drop the highlight so the next click starts a fresh selection
        // rather than extending the same range, and so the user gets a
        // clear "copy complete" cue (the selected text disappears).
        owner.ClearSelection();
    }

    /// <summary>
    /// Shows the "Copied" toast — the same shape as the official ToastDemo
    /// (<c>ToastService.Show</c>). <see cref="ToastHostSentinel"/> (installed
    /// once in <c>PhiTuiApp.Run</c>) keeps the ToastHost animation clock warm
    /// so this always appears, even when copies are seconds apart.
    /// </summary>
    private static void ShowCopiedToast(string message)
    {
        ToastService.Show(() => new Toast
        {
            Title = "Copied to clipboard",
            Content = message,
            Severity = ToastSeverity.Info,
            ShowProgress = false,
            Duration = TimeSpan.FromSeconds(2),
        });
    }
}
