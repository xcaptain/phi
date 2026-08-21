using Phi.Tui;

namespace Phi.Tests.Tui;

/// <summary>
/// Verifies that <see cref="SystemClipboard"/> actually hands text to the
/// OS clipboard via <c>pbcopy</c> on macOS. The harness here is intentionally
/// not isolated to a recorder — it round-trips through the real shell so
/// regressions in the platform helper are caught.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SystemClipboardTests
{
    [Test]
    public async Task TrySetText_RoutesThroughOverride_WhenInstalled()
    {
        var captured = new List<string>();
        var previous = SystemClipboard.Override;
        try
        {
            SystemClipboard.Override = text =>
            {
                captured.Add(text);
                return true;
            };

            var ok = SystemClipboard.TrySetText("hello world");

            await Assert.That(ok).IsTrue();
            await Assert.That(captured).IsEquivalentTo(["hello world"]);
        }
        finally
        {
            SystemClipboard.Override = previous;
        }
    }

    [Test]
    public async Task TrySetText_NullText_ReturnsFalse()
    {
        var previous = SystemClipboard.Override;
        try
        {
            SystemClipboard.Override = _ => true;
            var ok = SystemClipboard.TrySetText(null!);
            await Assert.That(ok).IsFalse();
        }
        finally
        {
            SystemClipboard.Override = previous;
        }
    }

    [Test]
    public async Task TrySetText_SwallowsOverrideExceptions_AndReturnsFalse()
    {
        // A misbehaving override (e.g. one that throws) must not crash the
        // UI thread; the caller gets a clean `false` so it can decide
        // whether to surface a toast.
        var previous = SystemClipboard.Override;
        var captured = new List<string>();
        Func<string, bool>? recordedOverride = null;
        try
        {
            recordedOverride = text =>
            {
                captured.Add(text);
                throw new InvalidOperationException("boom");
            };
            SystemClipboard.Override = recordedOverride;

            var ok = SystemClipboard.TrySetText("text");

            await Assert.That(ok).IsFalse();
            await Assert.That(captured).IsEquivalentTo(["text"]);
            // Override is unchanged — TrySetText doesn't swap it out.
            await Assert.That(SystemClipboard.Override).IsSameReferenceAs(recordedOverride);
        }
        finally
        {
            SystemClipboard.Override = previous;
        }
    }

    [Test]
    public async Task TrySetText_Pbcopy_RoundTripsThroughOSClipboard_OnMacOS()
    {
        // Only meaningful on macOS where pbcopy is the production path.
        // Skip on other platforms to avoid false negatives — the
        // Windows/Linux branches are exercised manually on those OSes.
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var previous = SystemClipboard.Override;
        try
        {
            // Detach any override so the production pbcopy path runs.
            SystemClipboard.Override = null;

            var ok = SystemClipboard.TrySetText("phi-clipboard-roundtrip");

            await Assert.That(ok).IsTrue();

            // Read back via pbpaste to confirm the bytes actually landed
            // in the OS clipboard rather than only being flushed to a
            // half-dead pipe.
            var (exit, stdout) = await RunPbpasteAsync();
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(stdout).IsEqualTo("phi-clipboard-roundtrip");
        }
        finally
        {
            SystemClipboard.Override = previous;
        }
    }

    private static async Task<(int ExitCode, string Stdout)> RunPbpasteAsync()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pbpaste",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
        {
            return (-1, "");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout);
    }
}
