using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Phi.Chat;
using Phi.Extensions;
using Phi.Extensions.Host;

namespace Phi.Avalonia;

/// <summary>
/// Avalonia implementation of <see cref="IUiSink"/>. Wires every
/// <see cref="IPhiUiBridge"/> method to the Avalonia shell's existing UI:
/// notifications go to <see cref="DeskLog"/> (always-on file logger, no
/// interruption), status / errors go to a transient in the chat page's
/// transient slot, and transcript lines render as a persistent info card
/// via <c>ChatTranscriptProjector.SubmitPersistentError</c> (until Sprint 4
/// introduces <c>CustomLine</c> + type-specific renderers). Dialogs are
/// modal <see cref="Window"/>s that resolve when the user closes them.
/// <para>
/// Constructed once by the Avalonia composition root
/// (<c>Phi.Avalonia.Desktop.Program.cs</c>) and passed to
/// <see cref="PhiUiBridge"/>, which becomes the
/// <see cref="ExtensionRuntime"/>'s UI bridge.
/// </para>
/// </summary>
internal sealed class AvaloniaUiSink : IUiSink
{
    private readonly ChatTranscriptProjector _projector;
    private readonly Func<Window?> _mainWindowAccessor;

    public bool HasUi => true;

    public AvaloniaUiSink(
        ChatTranscriptProjector projector,
        Func<Window?> mainWindowAccessor)
    {
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(mainWindowAccessor);
        _projector = projector;
        _mainWindowAccessor = mainWindowAccessor;
    }

    public void Notify(string message, NotifyLevel level)
    {
        var prefix = level switch
        {
            NotifyLevel.Warning => "⚠",
            NotifyLevel.Error => "✗",
            _ => "ℹ",
        };
        DeskLog.Write($"extension.notify[{level}]: {prefix} {message}");
        // Info notifications stay in DeskLog only — landing them in the
        // transcript as a permanent line would clutter the chat history
        // for low-signal notices. Warning / Error do deserve a transcript
        // entry because they're actionable: persistent + scannable in
        // scrollback. Sprint 4 introduces a proper transient slot so
        // even these get out of the transcript when the user acknowledges.
        if (level == NotifyLevel.Info) return;
        PostUi(() => _projector.SubmitPersistentError($"{prefix} {message}"));
    }

    public void NotifyStatus(string message)
    {
        DeskLog.Write($"extension.status: {message}");
        PostUi(() => _projector.SubmitPersistentError(message));
    }

    public void FlashError(string message, bool persistent)
    {
        DeskLog.Write($"extension.error(persistent={persistent}): {message}");
        PostUi(() =>
        {
            // Persistent errors land in the transcript so they don't get
            // lost when the transient slot clears on the next state change.
            // Sprint 4 will route via CustomLine + a proper error renderer;
            // this keeps the path observable today.
            if (persistent)
                _projector.SubmitPersistentError($"⚠ {message}");
        });
    }

    public void SubmitTranscriptLine(TranscriptLine line)
    {
        DeskLog.Write($"extension.transcript[{line.Type}]: {line.Content}");
        // Sprint 4: the line lands in the projector as a CustomLine. The
        // transcript renderer dispatches by LineType to whatever renderer
        // the extension registered; without one it falls back to a plain
        // text bubble. (Before Sprint 4 this routed to the persistent
        // error slot, which polluted the transcript with non-error lines.)
        PostUi(() => _projector.SubmitCustomLine(line.Type, line.Id, line.Content, line.Details));
    }

    public void SubmitCustomMessageLine(
        string customType,
        string content,
        IReadOnlyDictionary<string, object?>? details)
    {
        DeskLog.Write($"extension.custom_message[{customType}]: {content}");
        PostUi(() => _projector.SubmitCustomMessageLine(customType, content, details));
    }

    public async Task<string?> ShowSelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
    {
        if (options.Count == 0) return null;
        return await ShowDialogAsync<string?>(title, timeout, w =>
        {
            var list = new ListBox { ItemsSource = options };
            list.DoubleTapped += (_, _) =>
            {
                if (list.SelectedItem is string s) w.Close(s);
            };
            return list;
        });
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout)
    {
        var result = await ShowDialogAsync<bool?>(title, timeout, w =>
        {
            var yes = new Button { Content = "Yes" };
            var no = new Button { Content = "No" };
            yes.Click += (_, _) => w.Close(true);
            no.Click += (_, _) => w.Close(false);
            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { yes, no },
                    },
                },
            };
        });
        return result ?? false;
    }

    public async Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout)
    {
        return await ShowDialogAsync<string?>(title, timeout, w =>
        {
            var textBox = new TextBox { Watermark = placeholder, Text = placeholder };
            var ok = new Button { Content = "OK" };
            var cancel = new Button { Content = "Cancel" };
            ok.Click += (_, _) => w.Close(textBox.Text);
            cancel.Click += (_, _) => w.Close(null);
            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Children =
                {
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { ok, cancel },
                    },
                },
            };
        });
    }

    private async Task<TResult?> ShowDialogAsync<TResult>(
        string title,
        TimeSpan? timeout,
        Func<Window, Control> buildBody)
    {
        var owner = _mainWindowAccessor();
        if (owner is null)
        {
            DeskLog.Write($"extension.dialog: no main window; returning default for '{title}'");
            return default;
        }

        var dialog = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dialog.Content = new Border
        {
            Padding = new Thickness(16),
            Child = buildBody(dialog),
        };

        var dialogTcs = dialog.ShowDialog<TResult?>(owner);
        if (timeout is { } ts)
        {
            var ms = (int)ts.TotalMilliseconds;
            _ = Task.Run(async () =>
            {
                await Task.Delay(ms);
                PostUi(() => dialog.Close());
            });
        }

        return await dialogTcs;
    }

    private static void PostUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }
}
