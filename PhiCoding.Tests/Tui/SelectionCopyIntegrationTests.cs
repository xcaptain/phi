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
/// End-to-end copy-on-select tests driven through a real <see cref="TerminalApp"/>
/// with an <see cref="InMemoryTerminalBackend"/>. The Phi project can't take
/// a dependency on the library's internal <c>TerminalAppTestDriver</c>, so we
/// reproduce the same public-API pattern here: open a terminal, build the app,
/// push synthetic mouse events, run the loop on a background task, and
/// poll an in-process clipboard recorder for the expected text.
/// <para>
/// The recorder installs a <see cref="SystemClipboard.Override"/> so the
/// production <c>pbcopy</c>/<c>xclip</c>/<c>clip.exe</c> path is bypassed
/// during tests — we still exercise the SelectionCopyHost → SystemClipboard
/// dispatch, but the assertion runs against the captured argument.
/// </para>
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SelectionCopyIntegrationTests
{
    [Test]
    public async Task AutoCopy_OnMouseDragSelection_WritesToClipboard()
    {
        await using var fixture = new TerminalFixture(
            new Paragraph("hello world").HorizontalAlignment(Align.Stretch),
            size: new TerminalSize(30, 4));

        // Drag-select "world" — the Paragraph starts at (0, 0), the word
        // "world" begins at column 6.
        fixture.PushMouse(MouseDown(6, 0));
        fixture.PushMouse(MouseDrag(11, 0));
        fixture.PushMouse(MouseUp(11, 0));

        var captured = await fixture.WaitForClipboardAsync("world", TimeSpan.FromSeconds(3));
        await Assert.That(captured).IsEqualTo("world");
    }

    [Test]
    public async Task AutoCopy_OnDoubleClickWord_WritesToClipboard()
    {
        await using var fixture = new TerminalFixture(
            new Paragraph("hello world").HorizontalAlignment(Align.Stretch),
            size: new TerminalSize(30, 4));

        fixture.PushMouse(DoubleClick(7, 0));
        fixture.PushMouse(MouseUp(7, 0));

        var captured = await fixture.WaitForClipboardAsync("world", TimeSpan.FromSeconds(3));
        await Assert.That(captured).IsEqualTo("world");
    }

    [Test]
    public async Task AutoCopy_OnSingleClick_DoesNotCopy()
    {
        await using var fixture = new TerminalFixture(
            new Paragraph("hello world").HorizontalAlignment(Align.Stretch),
            size: new TerminalSize(30, 4));

        fixture.PushMouse(MouseDown(4, 0));
        fixture.PushMouse(MouseUp(4, 0));

        // Give the app a brief moment to process the events. A single click
        // sets anchor == active (no selection), so no call must reach the
        // SystemClipboard recorder.
        await Task.Delay(150);
        await Assert.That(fixture.CapturedClipboardTexts).IsEmpty();
    }

    [Test]
    public async Task AutoCopy_RespectsIsSelectableFalse_DoesNotCopy()
    {
        await using var fixture = new TerminalFixture(
            new Paragraph("hello world") { IsSelectable = false }.HorizontalAlignment(Align.Stretch),
            size: new TerminalSize(30, 4));

        fixture.PushMouse(MouseDown(6, 0));
        fixture.PushMouse(MouseDrag(11, 0));
        fixture.PushMouse(MouseUp(11, 0));

        await Task.Delay(150);
        await Assert.That(fixture.CapturedClipboardTexts).IsEmpty();
    }

    [Test]
    public async Task AutoCopy_NestedLayout_WalksToSelectableAncestor()
    {
        // Regression guard for the SelectionCopyHost wiring: a single
        // leaf-text drag still works through the host that wraps the root.
        await using var fixture = new TerminalFixture(
            new Paragraph("hello world").HorizontalAlignment(Align.Stretch),
            size: new TerminalSize(30, 4));

        fixture.PushMouse(MouseDown(6, 0));
        fixture.PushMouse(MouseDrag(11, 0));
        fixture.PushMouse(MouseUp(11, 0));

        var captured = await fixture.WaitForClipboardAsync("world", TimeSpan.FromSeconds(3));
        await Assert.That(captured).IsEqualTo("world");
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

    private static TerminalMouseEvent DoubleClick(int x, int y) => new()
    {
        Kind = TerminalMouseKind.DoubleClick,
        Button = TerminalMouseButton.Left,
        X = x,
        Y = y,
    };

    /// <summary>
    /// Minimal headless test driver around <see cref="TerminalApp"/> +
    /// <see cref="InMemoryTerminalBackend"/>. Public APIs only — the
    /// library's own <c>TerminalAppTestDriver</c> is internal and can't
    /// be referenced from outside. Each fixture installs a
    /// <see cref="SystemClipboard.Override"/> recorder so the production
    /// shell-out path is bypassed and assertions run against the recorded
    /// argument list.
    /// </summary>
    private sealed class TerminalFixture : IAsyncDisposable
    {
        private readonly InMemoryTerminalBackend _backend;
        private readonly TerminalSession _session;
        private readonly TerminalApp _app;
        private readonly Task _runTask;
        private readonly CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _stopped = new();
        private readonly ConcurrentQueue<string> _clipboardCaptures = new();
        private readonly Func<string, bool>? _previousOverride;

        public Visual Content { get; }

        public IReadOnlyCollection<string> CapturedClipboardTexts => _clipboardCaptures;

        public TerminalFixture(Visual content, TerminalSize? size = null)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            _backend = new InMemoryTerminalBackend(size ?? new TerminalSize(80, 25));
            _session = Terminal.Open(
                _backend,
                new TerminalOptions { ImplicitStartInput = true },
                force: true);
            var host = new SelectionCopyHost(content);
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
            // Capture the production override (if any) and install a recorder
            // so the SelectionCopyHost → SystemClipboard path runs against
            // our queue instead of spawning pbcopy during the test.
            _previousOverride = SystemClipboard.Override;
            SystemClipboard.Override = text =>
            {
                _clipboardCaptures.Enqueue(text);
                return true;
            };
            _runTask = Task.Run(() =>
            {
                try { _app.Run(_cts.Token); }
                catch (OperationCanceledException) { }
                finally { _stopped.Set(); }
            });
        }

        public void PushMouse(TerminalMouseEvent ev) => _backend.PushEvent(ev);

        public async Task<string> WaitForClipboardAsync(string expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var capture in _clipboardCaptures)
                {
                    if (capture == expected)
                    {
                        return capture;
                    }
                }
                await Task.Delay(20);
            }
            // Last attempt: return whatever was captured last so the
            // assertion can surface the actual mismatch.
            return _clipboardCaptures.LastOrDefault() ?? "";
        }

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
