using System.Globalization;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;
using Phi.Tui;

namespace Phi.Tests.Tui;

/// <summary>
/// End-to-end <see cref="TuiDialogShower"/> tests driven through a real
/// <see cref="TerminalApp"/> with an <see cref="InMemoryTerminalBackend"/>.
/// Proves the dialog shower can be invoked from a worker thread (the
/// exact path PermissionGate's async <c>tool_call</c> hook hits when it
/// awaits <c>ctx.Ui.ConfirmAsync</c> from inside
/// <see cref="HookRegistry"/>'s <c>GetAwaiter().GetResult()</c>) without
/// tripping XenoAtom's <c>Invalid thread access. Use
/// <see cref="TerminalApp.Dispatcher"/> to marshal to the UI thread.</c>
/// access check.
/// <para>
/// We never interact with the dialog UI itself — we only assert that
/// <see cref="TuiDialogShower"/> survives the cross-thread boundary and
/// returns the no-op default (<c>null</c> / <c>false</c>) on timeout.
/// Real user-driven dialog flows live in interactive TUI smoke tests.
/// </para>
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class TuiDialogShowerIntegrationTests
{
    [Test]
    public async Task ConfirmAsync_FromWorkerThread_OnTimeout_ReturnsFalseWithoutThrowing()
    {
        await using var fx = new DialogFixture();
        var shower = new TuiDialogShower(() => fx.App);

        // Race: dispatch from a worker thread, await on the test thread.
        // The dialog plumbing must complete without throwing
        // "Invalid thread access".
        var task = Task.Run(async () =>
            await shower.ShowConfirmAsync(
                title: "Permission Gate",
                message: "Allow `rm -rf /`?",
                timeout: TimeSpan.FromMilliseconds(200)));

        // The task MUST NOT surface an InvalidThreadAccess exception; it
        // should resolve to false (the TCS default) when the timeout
        // fires before any user interaction.
        bool result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ShowInputAsync_FromWorkerThread_OnTimeout_ReturnsNullWithoutThrowing()
    {
        await using var fx = new DialogFixture();
        var shower = new TuiDialogShower(() => fx.App);

        var task = Task.Run(async () =>
            await shower.ShowInputAsync(
                title: "API Key",
                placeholder: "sk-…",
                timeout: TimeSpan.FromMilliseconds(200)));

        string? result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ShowSelectAsync_FromWorkerThread_OnTimeout_ReturnsNullWithoutThrowing()
    {
        await using var fx = new DialogFixture();
        var shower = new TuiDialogShower(() => fx.App);

        var task = Task.Run(async () =>
            await shower.ShowSelectAsync(
                title: "Pick a model",
                options: new[] { "gpt-4o", "claude-sonnet", "gemini-pro" },
                timeout: TimeSpan.FromMilliseconds(200)));

        string? result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// Headless TerminalApp harness mirroring the
    /// SelectionCopyIntegrationTests fixture pattern: in-memory terminal
    /// backend, the loop runs on a background task, and we never write
    /// to stdout. Sufficient for verifying that
    /// <see cref="TuiDialogShower"/>'s cross-thread marshal works — the
    /// dialog itself is shown against the in-memory backend (no real
    /// terminal needed) and dismissed via timeout.
    /// </summary>
    private sealed class DialogFixture : IAsyncDisposable
    {
        private readonly InMemoryTerminalBackend _backend;
        private readonly TerminalSession _session;
        private readonly Task _runTask;
        private readonly CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _stopped = new();

        public TerminalApp App { get; }

        public DialogFixture()
        {
            _backend = new InMemoryTerminalBackend(new TerminalSize(80, 25));
            _session = Terminal.Open(
                _backend,
                new TerminalOptions { ImplicitStartInput = true },
                force: true);

            var host = new VStack(); // dummy root — we never render it
            App = new TerminalApp(host, _session.Instance, new TerminalAppOptions
            {
                HostKind = TerminalHostKind.Fullscreen,
                RawMode = TerminalRawModeKind.CBreak,
                DisableInputEcho = true,
                EnableMouse = false,
                InitialFocusMode = InitialFocusMode.None,
                Culture = CultureInfo.InvariantCulture,
                LoopMode = TerminalLoopMode.Auto,
                UpdateWaitDuration = TimeSpan.FromMilliseconds(1),
                WideRuneResolver = TerminalWideRuneResolvers.Default,
            });

            _runTask = Task.Run(() =>
            {
                try { App.Run(_cts.Token); }
                catch (OperationCanceledException) { }
                finally { _stopped.Set(); }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { App.Stop(); } catch { /* may already be stopping */ }
            await Task.Run(() => _stopped.Wait(TimeSpan.FromSeconds(2)));
            _session.Dispose();
            _cts.Dispose();
        }
    }
}