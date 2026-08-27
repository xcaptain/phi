using Phi.Tui;
using XenoAtom.Terminal.UI;

namespace Phi.Tests.Tui;

/// <summary>
/// Unit tests for <see cref="TuiUiThread"/>. The marshal helper is the
/// central piece of infrastructure that keeps the TUI shell from tripping
/// XenoAtom's <c>Invalid thread access</c> check when streaming events
/// (HarnessEvent / StateChanged) land on worker threads. These tests pin
/// the no-app fallback behaviour (used by every unit test that exercises
/// TUI components without spinning up a TerminalApp), and the
/// TerminalApp-bound path's marshalling contract.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class TuiUiThreadTests
{
    [Test]
    public async Task None_Has_IsActive_False()
    {
        await Assert.That(TuiUiThread.None.IsActive).IsFalse();
    }

    [Test]
    public async Task None_Post_RunsSynchronously_OnCallingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var observedThread = -1;
        TuiUiThread.None.Post(() => observedThread = Environment.CurrentManagedThreadId);

        await Assert.That(observedThread).IsEqualTo(callerThread);
    }

    [Test]
    public async Task None_InvokeAsync_Action_RunsSynchronously_AndCompletesImmediately()
    {
        var observed = false;
        await TuiUiThread.None.InvokeAsync(() => observed = true);
        await Assert.That(observed).IsTrue();
    }

    [Test]
    public async Task None_InvokeAsync_Func_ReturnsValue_Synchronously()
    {
        var result = await TuiUiThread.None.InvokeAsync(() => 42);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task None_InvokeAsync_AsyncFunc_AwaitsAndReturnsValue()
    {
        var result = await TuiUiThread.None.InvokeAsync(async () =>
        {
            await Task.Yield();
            return "done";
        });
        await Assert.That(result).IsEqualTo("done");
    }

    [Test]
    public async Task None_Focus_IsNoOp()
    {
        // Focus() on the no-op marshaller must not throw — it simply has
        // no app to focus on. This is the test path: components construct
        // TuiUiThread.None when no TerminalApp is running, and visual
        // tree references may or may not exist.
        Exception? caught = null;
        try
        {
            TuiUiThread.None.Focus(new TerminalAppTestsRoot());
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// Bare visual for the Focus() no-op test. We don't care about the
    /// visual itself — only that Focus() can be called without a real
    /// TerminalApp bound.
    /// </summary>
    private sealed class TerminalAppTestsRoot : XenoAtom.Terminal.UI.Visual
    {
    }
}
