using Avalonia;
using Avalonia.Headless;

namespace Phi.Avalonia.Tests;

/// <summary>
/// Boots the Avalonia headless platform once per test process. Without an
/// xUnit/NUnit adapter (the repo runs TUnit), tests that touch controls
/// must serialize via <c>[NotInParallel]</c> and run inline dispatchers.
/// </summary>
public static class AvaloniaTestHost
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized) return;
            AppBuilder.Configure<PhiAvaloniaApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _initialized = true;
        }
    }
}
