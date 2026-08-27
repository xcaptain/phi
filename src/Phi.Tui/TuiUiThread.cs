using XenoAtom.Terminal.UI;

namespace Phi.Tui;

/// <summary>
/// Marshals actions back to the XenoAtom <see cref="TerminalApp"/>'s UI
/// thread. XenoAtom's visual tree is bound to its dispatcher's thread — any
/// <c>State&lt;T&gt;.Value = …</c>, <c>BindableList&lt;T&gt;.Add(…)</c>,
/// <c>Dialog.Show()</c>, <c>Visual.Focus(…)</c> call from a worker thread
/// throws <c>Invalid thread access. Use TerminalApp.Dispatcher to marshal
/// to the UI thread.</c>
/// <para>
/// This helper centralizes the marshal pattern so TUI components
/// (<c>ChatTranscript</c>, <c>ChatHeader</c>, <c>PhiStatusBar</c>,
/// <c>PromptInput</c>) don't each need to take a <see cref="TerminalApp"/>
/// reference. The shell creates one per chat page and passes it down.
/// </para>
/// <para>
/// <b>Thread model:</b> the shell wires this on the UI thread inside
/// <c>PhiTuiApp.Run()</c>; the marshalled action runs against the live
/// <see cref="XenoAtom.Terminal.UI.Threading.Dispatcher"/> attached to the
/// app, which fires its <c>VerifyAccess</c> check after the Post lands.
/// </para>
/// <para>
/// <b>Nullable in tests:</b> the marshal is optional. Components accept a
/// null <see cref="TuiUiThread"/> and fall back to running synchronously on
/// the calling thread — that's how the existing
/// <c>PhiTuiAppTests</c>/<c>PhiStatusBarTests</c> tests stay green without
/// spinning up a <see cref="TerminalApp"/>. The marshal only kicks in
/// when a real UI thread is present.
/// </para>
/// </summary>
public class TuiUiThread
{
    private readonly Func<TerminalApp?> _appAccessor;

    /// <summary>
    /// Creates a thread marshaller bound to <paramref name="app"/>. Use
    /// this overload in production paths.
    /// </summary>
    public TuiUiThread(TerminalApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _appAccessor = () => app;
    }

    /// <summary>
    /// Creates a thread marshaller that resolves the app lazily. Use this
    /// overload when the <see cref="TerminalApp"/> is not yet constructed
    /// (e.g. the dialog shower wired before <c>Terminal.Run</c> starts).
    /// </summary>
    public TuiUiThread(Func<TerminalApp?> appAccessor)
    {
        ArgumentNullException.ThrowIfNull(appAccessor);
        _appAccessor = appAccessor;
    }

    /// <summary>
    /// Creates a no-op marshaller. Calls land on the calling thread
    /// synchronously — the test path. The component still type-checks the
    /// parameter so the test cannot accidentally bypass marshalling in
    /// production code (production always passes a non-null instance).
    /// </summary>
    public static TuiUiThread None { get; } = new(appAccessor: () => null);

    /// <summary>
    /// True when a real <see cref="TerminalApp"/> has been bound. False
    /// for the <see cref="None"/> instance or before the lazy accessor
    /// resolves. Tests rely on <c>IsActive == false</c> to keep their
    /// synchronous assertions working.
    /// </summary>
    public bool IsActive => _appAccessor?.Invoke() is not null;

    /// <summary>
    /// Sets focus on <paramref name="visual"/> through the live
    /// <see cref="TerminalApp"/>, on the UI thread. Synchronous fallback
    /// when no app is bound (test path). Used by the dialog shower's
    /// initial focus (Yes / textbox) — the same marshalling rule applies
    /// as for visual mutations.
    /// </summary>
    public void Focus(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        var app = _appAccessor?.Invoke();
        if (app is null)
        {
            return;
        }
        app.Focus(visual);
    }

    /// <summary>
    /// Posts <paramref name="action"/> to the UI thread without awaiting
    /// it. Use this for fire-and-forget UI updates driven by streaming
    /// events (<c>HarnessEvent</c>); the UI thread will pick the action
    /// up on its next dispatcher tick.
    /// </summary>
    public virtual void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var app = _appAccessor?.Invoke();
        if (app is null)
        {
            // No app bound — run synchronously. This is the test path:
            // existing tests construct components without a TerminalApp
            // and rely on synchronous state propagation to make
            // assertions against the visual tree immediately after the
            // event fires.
            action();
            return;
        }
        app.Dispatcher.Post(action);
    }

    /// <summary>
    /// Invokes <paramref name="action"/> on the UI thread and returns a
    /// task that completes when the action finishes. Use this when the
    /// caller needs the result (e.g. awaiting a dialog closure); for
    /// streaming UI updates prefer <see cref="Post"/>.
    /// </summary>
    public virtual Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var app = _appAccessor?.Invoke();
        if (app is null)
        {
            // Same rationale as Post — test path runs synchronously.
            action();
            return Task.CompletedTask;
        }
        return app.Dispatcher.InvokeAsync(action);
    }

    /// <summary>
    /// Invokes <paramref name="func"/> on the UI thread and returns its
    /// result. Synchronous fallback when no app is bound.
    /// </summary>
    public virtual Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var app = _appAccessor?.Invoke();
        if (app is null)
        {
            return Task.FromResult(func());
        }
        return app.Dispatcher.InvokeAsync(func);
    }

    /// <summary>
    /// Invokes <paramref name="func"/> on the UI thread, awaits it, and
    /// returns its result. The async overload of XenoAtom's dispatcher
    /// preserves the "run on UI thread" semantics across awaits inside
    /// the delegate. Used by the dialog shower, which constructs its
    /// <see cref="TaskCompletionSource{TResult}"/> inside the closure and
    /// awaits the user-dismiss task from the same thread.
    /// </summary>
    public virtual async Task<T> InvokeAsync<T>(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var app = _appAccessor?.Invoke();
        if (app is null)
        {
            // Test path: just await the func on the current thread.
            // Production callers always go through the dispatcher branch.
            return await func();
        }
        return await app.Dispatcher.InvokeAsync(func);
    }
}
