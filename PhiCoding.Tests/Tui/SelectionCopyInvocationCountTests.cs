using System.Collections.Concurrent;
using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;
using PhiCoding.Tui;

namespace PhiCoding.Tests.Tui;

/// <summary>
/// Regression guard for the "first copy shows two toasts" bug: each pointer
/// release must trigger exactly one <see cref="SelectionCopyHost"/>
/// notification even when wrapped by sibling <see cref="ContentVisual"/>s.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SelectionCopyInvocationCountTests
{
    [Test]
    public async Task DragRelease_InvokesHandler_ExactlyOnce()
    {
        var copies = new ConcurrentBag<string>();

        await using var fixture = new TerminalFixture(
            new Paragraph("hello world").HorizontalAlignment(Align.Stretch),
            size: new TerminalSize(30, 4),
            onCopy: text => copies.Add(text));

        fixture.PushMouse(MouseDown(6, 0));
        fixture.PushMouse(MouseDrag(11, 0));
        fixture.PushMouse(MouseUp(11, 0));

        await Task.Delay(200);

        await Assert.That(copies).IsEquivalentTo(["world"]);
    }

    private static TerminalMouseEvent MouseDown(int x, int y) => new()
    {
        Kind = TerminalMouseKind.Down,
        Button = TerminalMouseButton.Left,
        X = x,
        Y = y,
    };

    private static TerminalMouseEvent MouseDrag(int x, int y) => new()
    {
        Kind = TerminalMouseKind.Drag,
        Button = TerminalMouseButton.Left,
        X = x,
        Y = y,
    };

    private static TerminalMouseEvent MouseUp(int x, int y) => new()
    {
        Kind = TerminalMouseKind.Up,
        Button = TerminalMouseButton.Left,
        X = x,
        Y = y,
    };

    private sealed class TerminalFixture : IAsyncDisposable
    {
        private readonly InMemoryTerminalBackend _backend;
        private readonly TerminalSession _session;
        private readonly TerminalApp _app;
        private readonly Task _runTask;
        private readonly CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _stopped = new();
        private readonly Func<string, bool>? _previousOverride;

        public Visual Content { get; }

        public TerminalFixture(Visual content, TerminalSize? size, Action<string> onCopy)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            _backend = new InMemoryTerminalBackend(size ?? new TerminalSize(80, 25));
            _session = Terminal.Open(
                _backend,
                new TerminalOptions { ImplicitStartInput = true },
                force: true);
            var host = new SelectionCopyHost(content);
            _previousOverride = SystemClipboard.Override;
            SystemClipboard.Override = text =>
            {
                onCopy(text);
                return true;
            };
            _app = new TerminalApp(host, _session.Instance, new TerminalAppOptions
            {
                HostKind = TerminalHostKind.Fullscreen,
                RawMode = TerminalRawModeKind.CBreak,
                DisableInputEcho = true,
                EnableMouse = true,
                MouseMode = TerminalMouseMode.Move,
                EnableBracketedPaste = true,
                InitialFocusMode = InitialFocusMode.None,
                Culture = CultureInfo.InvariantCulture,
                LoopMode = TerminalLoopMode.Auto,
                UpdateWaitDuration = TimeSpan.FromMilliseconds(1),
                WideRuneResolver = TerminalWideRuneResolvers.Default,
            });
            _runTask = Task.Run(() =>
            {
                try { _app.Run(_cts.Token); }
                catch (OperationCanceledException) { }
                finally { _stopped.Set(); }
            });
        }

        public void PushMouse(TerminalMouseEvent ev) => _backend.PushEvent(ev);

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _app.Stop(); } catch { /* may already be stopping */ }
            await Task.Run(() => _stopped.Wait(TimeSpan.FromSeconds(2)));
            _session.Dispose();
            _cts.Dispose();
            SystemClipboard.Override = _previousOverride;
        }
    }
}
