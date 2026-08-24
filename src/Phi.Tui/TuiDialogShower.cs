using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tui;

/// <summary>
/// Builds the three modal dialogs the <see cref="IPhiUiBridge"/> needs
/// (select / confirm / input) using XenoAtom primitives, returning
/// <c>Task&lt;T&gt;</c> the dialog resolves on close. Used by
/// <see cref="TuiUiSink"/>; the built-in slash-command dialogs in
/// <c>PromptInput.Dialogs</c> are unchanged (they keep their callback
/// style and their own UX flourishes like API-key pre-fill).
/// <para>
/// All XenoAtom interactions (constructing the dialog, calling
/// <c>Show()</c>, <c>Close()</c>, focus changes) are marshalled to the
/// <see cref="TerminalApp"/>'s UI thread via
/// <see cref="XenoAtom.Terminal.UI.Threading.Dispatcher.InvokeAsync{T}"/>.
/// Calling threads are typically background workers — for example the
/// extension runtime's hook chain invokes <c>PermissionGate</c>'s
/// <c>ConfirmAsync</c> from <see cref="HookRegistry"/>'s
/// <c>GetAwaiter().GetResult()</c> — so without this marshal, dialog
/// construction would throw <c>Invalid thread access</c>.
/// </para>
/// <para>
/// <b>Timeout semantics:</b> the timeout only completes the
/// <see cref="TaskCompletionSource{TResult}"/> with the no-op default
/// (null / false). It does <em>not</em> close the dialog from a worker
/// thread (which would re-trip the access check); the user can still
/// dismiss the dialog manually and the late TCS result is harmlessly
/// ignored via <c>TrySetResult</c>.
/// </para>
/// </summary>
public sealed class TuiDialogShower
{
    /// <summary>The XenoAtom app hosting the dialogs.</summary>
    private readonly Func<TerminalApp> _appAccessor;

    public TuiDialogShower(Func<TerminalApp> appAccessor)
    {
        ArgumentNullException.ThrowIfNull(appAccessor);
        _appAccessor = appAccessor;
    }

    public Task<string?> ShowSelectAsync(
        string title,
        IReadOnlyList<string> options,
        TimeSpan? timeout)
    {
        if (options.Count == 0) return Task.FromResult<string?>(null);

        return MarshalAsync(_appAccessor, async () =>
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var list = new OptionList<OptionListItem>().ActivateOnClick(true);
            foreach (var o in options) list.Items.Add(new OptionListItem(o));

            list.ItemActivated((_, e) =>
            {
                if ((uint)e.Index < (uint)options.Count)
                    tcs.TrySetResult(options[e.Index]);
                else
                    tcs.TrySetResult(null);
                if (list.Parent is Dialog d) d.Close();
            });

            var dialog = new Dialog(new Markup($"[bold]{Escape(title)}[/]"), list)
            {
                IsResizable = false,
                IsDraggable = true,
                IsModal = true,
            };
            dialog.KeyDownRouted += (_, ev) =>
            {
                if (ev.Key == TerminalKey.Escape)
                {
                    tcs.TrySetResult(null);
                    dialog.Close();
                }
            };
            dialog.Show();
            // Timeout is best-effort: returns null on deadline, dialog
            // continues to live and a late TCS result is ignored.
            StartTimeout(tcs, timeout);
            return await tcs.Task;
        });
    }

    public Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout)
    {
        return MarshalAsync(_appAccessor, async () =>
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var yesBtn = new Button("[bold]Yes[/]");
            var noBtn = new Button("No");
            var body = new VStack(
                new Markup(Escape(message)),
                new HStack(yesBtn, noBtn).Spacing(1)).Spacing(1);

            var dialog = new Dialog(new Markup($"[bold]{Escape(title)}[/]"), body)
            {
                IsResizable = false,
                IsDraggable = true,
                IsModal = true,
            };
            // Buttons fire ClickRouted on Enter, Space, or mouse press;
            // the Dialog's keydown handlers below cover the same logic
            // when the focus is on the body / dialog chrome instead.
            yesBtn.ClickRouted += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
            noBtn.ClickRouted  += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
            dialog.KeyDownRouted += (_, ev) =>
            {
                switch (ev.Key)
                {
                    case TerminalKey.Enter:
                        tcs.TrySetResult(true);
                        dialog.Close();
                        break;
                    case TerminalKey.Escape:
                        tcs.TrySetResult(false);
                        dialog.Close();
                        break;
                }
            };
            dialog.Show();
            _appAccessor().Focus(yesBtn);
            StartTimeout(tcs, timeout);
            return await tcs.Task;
        });
    }

    public Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout)
    {
        return MarshalAsync(_appAccessor, async () =>
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var textBox = new TextBox { Text = placeholder };
            var okBtn = new Button("OK");
            var cancelBtn = new Button("Cancel");
            var body = new VStack(
                new Markup(Escape(placeholder)),
                textBox,
                new HStack(okBtn, cancelBtn).Spacing(1)).Spacing(1);

            var dialog = new Dialog(new Markup($"[bold]{Escape(title)}[/]"), body)
            {
                IsResizable = false,
                IsDraggable = true,
                IsModal = true,
            };
            okBtn.ClickRouted += (_, _) => { tcs.TrySetResult(textBox.Text); dialog.Close(); };
            cancelBtn.ClickRouted += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
            dialog.KeyDownRouted += (_, ev) =>
            {
                switch (ev.Key)
                {
                    case TerminalKey.Enter:
                        tcs.TrySetResult(textBox.Text);
                        dialog.Close();
                        break;
                    case TerminalKey.Escape:
                        tcs.TrySetResult(null);
                        dialog.Close();
                        break;
                }
            };
            dialog.Show();
            _appAccessor().Focus(textBox);
            StartTimeout(tcs, timeout);
            return await tcs.Task;
        });
    }

    /// <summary>
    /// Run <paramref name="func"/> entirely on the XenoAtom UI thread and
    /// propagate its result (or exception) back to the calling thread.
    /// XenoAtom's <see cref="Dialog"/> / <see cref="Visual"/> tree is
    /// bound to the app's dispatcher; any worker-thread access to
    /// <c>Show</c>, <c>Close</c>, <c>Focus</c>, or the visual tree throws
    /// <c>Invalid thread access. Use TerminalApp.Dispatcher to marshal to
    /// the UI thread.</c>. <see cref="XenoAtom.Terminal.UI.Threading.Dispatcher.InvokeAsync{T}(Func{Task{T}})"/>
    /// gives us the continuation-on-UI-thread semantic we need; calling
    /// thread just awaits the returned <see cref="Task{TResult}"/>.
    /// </summary>
    private static Task<T> MarshalAsync<T>(Func<TerminalApp> appAccessor, Func<Task<T>> func)
    {
        // Resolve the app once (the dispatcher is a property on the
        // app) so the lambda doesn't have to. Fail fast if the app isn't
        // available — the TUI host always provides one, so a null here
        // means a wiring error rather than a recoverable condition.
        var app = appAccessor() ?? throw new InvalidOperationException(
            "TuiDialogShower: TerminalApp is null; UI dialogs cannot be shown.");
        return app.Dispatcher.InvokeAsync(func);
    }

    /// <summary>
    /// Best-effort timeout: <see cref="Task.Delay(TimeSpan)"/> then
    /// <c>TrySetResult(default)</c>. The TCS was created with
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>,
    /// so the continuation runs on the threadpool — safe, since we're
    /// only completing a task (no UI access). A late user-driven result
    /// (e.g. they press Enter after the deadline) is harmlessly ignored
    /// because <c>TrySetResult</c> on an already-completed TCS is a
    /// no-op.
    /// </summary>
    private static void StartTimeout<T>(TaskCompletionSource<T> tcs, TimeSpan? timeout)
    {
        if (!timeout.HasValue) return;
        _ = Task.Delay(timeout.Value).ContinueWith(
            static (_, state) =>
            {
                var t = (TaskCompletionSource<T>)state!;
                t.TrySetResult(default!);
            },
            tcs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string Escape(string text)
        => text.Replace("[", "\\[").Replace("]", "\\]");
}