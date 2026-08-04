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
/// Regression guard for the "second copy shows no toast" bug. XenoAtom's
/// <c>ToastHost</c> has a stale-animation-clock bug: once its toast entries
/// go empty, the next toast added later is instantly dismissed. The fix keeps
/// an invisible sentinel toast alive so the clock never goes stale — this test
/// verifies a copy made after a full idle gap still produces a toast that
/// survives (i.e. is not instantly dismissed).
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SelectionCopyStaleClockTests
{
    [Test]
    public async Task CopyAfterGap_SecondToastSurvives()
    {
        var paragraph = new Paragraph("hello world hello again").HorizontalAlignment(Align.Stretch);
        var toastHost = new ToastHost(new SelectionCopyHost(paragraph));
        toastHost.DefaultDuration = TimeSpan.FromSeconds(1);
        // Same workaround PhiTuiApp.Run applies: keep the ToastHost's
        // animation clock warm so a copy after an idle gap still toasts.
        ToastHostSentinel.Install(toastHost);

        var copies = new ConcurrentBag<string>();
        await using var fixture = new TerminalFixture(
            toastHost,
            size: new TerminalSize(60, 12),
            onCopy: text => copies.Add(text));

        // First copy: "world". This creates the invisible sentinel toast
        // plus the visible "Copied 5 chars" toast (2 entries).
        fixture.PushMouse(MouseDown(6, 0));
        fixture.PushMouse(MouseDrag(11, 0));
        fixture.PushMouse(MouseUp(11, 0));
        await fixture.WaitForClipboardCountAsync(1, TimeSpan.FromSeconds(3));
        await fixture.WaitForEntryCountAsync(2, TimeSpan.FromSeconds(3));

        // Let the visible toast fully expire (duration 1s), leaving only the
        // sentinel (entry count == 1). This idle period is what used to leave
        // the host's animation clock stale and instantly dismiss the next
        // toast.
        await Task.Delay(2500);
        Console.Error.WriteLine($"After first toast expired: entries={fixture.EntryCount}");
        await Assert.That(fixture.EntryCount).IsEqualTo(1);

        // Second copy: "again".
        fixture.PushMouse(MouseDown(18, 0));
        fixture.PushMouse(MouseDrag(23, 0));
        fixture.PushMouse(MouseUp(23, 0));
        await fixture.WaitForClipboardCountAsync(2, TimeSpan.FromSeconds(3));
        await Task.Delay(300);
        Console.Error.WriteLine($"300ms after second copy: entries={fixture.EntryCount}");

        // The second copy's toast must still be present (sentinel + toast,
        // count == 2) rather than being instantly dismissed by the stale
        // host clock.
        await Assert.That(fixture.EntryCount).IsEqualTo(2);
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
        private readonly ConcurrentQueue<string> _clipboard = new();
        private readonly ToastHost _toastHost;

        public TerminalFixture(Visual root, TerminalSize? size, Action<string> onCopy)
        {
            _toastHost = root as ToastHost ?? throw new ArgumentException("Root must be a ToastHost.", nameof(root));
            _backend = new InMemoryTerminalBackend(size ?? new TerminalSize(80, 25));
            _session = Terminal.Open(
                _backend,
                new TerminalOptions { ImplicitStartInput = true },
                force: true);
            _previousOverride = SystemClipboard.Override;
            SystemClipboard.Override = text =>
            {
                _clipboard.Enqueue(text);
                onCopy(text);
                return true;
            };
            _app = new TerminalApp(root, _session.Instance, new TerminalAppOptions
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

        public int EntryCount
        {
            get
            {
                var entriesField = typeof(ToastHost).GetField("_entries",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return entriesField?.GetValue(_toastHost) is System.Collections.ICollection c ? c.Count : -1;
            }
        }

        public void PushMouse(TerminalMouseEvent ev) => _backend.PushEvent(ev);

        public async Task WaitForClipboardCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_clipboard.Count >= count)
                    return;
                await Task.Delay(20);
            }
        }

        public async Task WaitForEntryCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (EntryCount <= count)
                    return;
                await Task.Delay(20);
            }
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
