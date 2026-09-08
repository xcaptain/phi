using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Phi.Avalonia.Desktop.ViewModels;

namespace Phi.Avalonia.Desktop.Views;

/// <summary>
/// Code-behind for the shell: handles the auto-scroll-to-end on chat
/// page swap. The shell is the only view that knows about both the routed
/// <see cref="ShellViewModel.CurrentPage"/> (which decides what to
/// render) and the <see cref="ScrollViewer"/> that hosts the rendered
/// chat page (which is where the scroll happens).
/// <para>
/// The chat page renders a plain <c>StackPanel</c> ItemsControl, so
/// every item is measured on the first layout pass and the
/// ScrollViewer's Extent is exact from the start.
/// <see cref="ScrollViewer.ScrollToEnd"/> on the next
/// <see cref="ScrollViewer.LayoutUpdated"/> pass lands the view
/// precisely on the last message. We hook <c>LayoutUpdated</c> once,
/// scroll, unhook — the unsubscribe happens BEFORE <c>ScrollToEnd</c>
/// so the scroll itself doesn't loop on the subsequent layout
/// invalidation it triggers.
/// </para>
/// </summary>
public partial class ShellView : UserControl
{
    private ScrollViewer? _transcriptScroll;
    private ChatPageViewModel? _watchedChat;

    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.PropertyChanged -= OnShellPropertyChanged;
            shell.PropertyChanged += OnShellPropertyChanged;
            WatchChatPage(shell.ChatPage);
            // The initial ChatPage is set in the ShellViewModel ctor
            // BEFORE the ShellView is even constructed, so the initial
            // ReadyForDisplay event fired before we subscribed. Trigger
            // a scroll now — LayoutUpdated hook below waits for the
            // next layout pass which is already pending for the first
            // paint.
            if (shell.ChatPage is not null)
                HookScrollOnNextLayout();
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.CurrentPage)
            or nameof(ShellViewModel.ChatPage))
        {
            WatchChatPage(((ShellViewModel)sender!).ChatPage);
            HookScrollOnNextLayout();
        }
    }

    /// <summary>Subscribe (or unsubscribe) to the active chat page's
    /// <see cref="ChatPageViewModel.ReadyForDisplay"/> event. Fires when
    /// the chat page projects a new message batch (initial attach or
    /// every StateChanged) — we defer-scroll in response.</summary>
    private void WatchChatPage(ChatPageViewModel? chat)
    {
        if (ReferenceEquals(chat, _watchedChat)) return;

        if (_watchedChat is not null)
            _watchedChat.ReadyForDisplay -= OnChatReadyForDisplay;
        _watchedChat = chat;
        if (chat is not null)
            chat.ReadyForDisplay += OnChatReadyForDisplay;
    }

    private void OnChatReadyForDisplay(object? sender, EventArgs e)
    {
        HookScrollOnNextLayout();
    }

    /// <summary>Schedule a one-shot scroll-to-end on the next layout
    /// pass. We can't call <c>ScrollToEnd</c> synchronously: the
    /// ItemsControl's new containers haven't been measured yet, so the
    /// ScrollViewer's Extent is stale (still 0 or the previous page's
    /// extent) and the scroll would land on the wrong offset.
    /// <see cref="ScrollViewer.LayoutUpdated"/> fires after the layout
    /// pass that includes the new content; we hook it once, scroll,
    /// unhook. The unsubscribe happens BEFORE <c>ScrollToEnd</c> so
    /// the scroll itself doesn't loop on the subsequent layout
    /// invalidation it triggers.</summary>
    private void HookScrollOnNextLayout()
    {
        if (_transcriptScroll is null)
            _transcriptScroll = this.FindControl<ScrollViewer>("TranscriptScroll");
        if (_transcriptScroll is null) return;

        EventHandler handler = null!;
        handler = (s, e) =>
        {
            _transcriptScroll!.LayoutUpdated -= handler;
            _transcriptScroll.ScrollToEnd();
        };
        _transcriptScroll.LayoutUpdated += handler;
    }
}