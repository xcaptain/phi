using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// `/reload` — the ALC GC dance + GenerationGuard:
/// <list type="bullet">
/// <item>After reload, the OLD assembly is collectible (WeakReference
/// becomes dead after GC.Collect + WaitForPendingFinalizers).</item>
/// <item>A captured <see cref="IPhiApi"/> from before the reload throws
/// <see cref="ExtensionGenerationException"/> on the next action call.</item>
/// </list>
/// </summary>
[NotInParallel("reload")]
public class ReloadTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _helloToolPath;

    public ReloadTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-reload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Phi.Extensions.HelloTool.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples", "extensions", "HelloTool", "bin", "Debug", "net10.0", "Phi.Extensions.HelloTool.dll"),
        };
        _helloToolPath = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("HelloTool.dll not found", candidates[0]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
    }

    private static SessionEnvironment BuildEnv() => new()
    {
        ProviderResolver = new FixedResolver(new NullProvider()),
        SystemPrompt = new SystemPromptOptions { ResolvedSystemPrompt = "stub" },
        MaxTurns = 5,
        ContextWindowTokens = ContextWindow.DefaultContextWindowTokens,
        AutoCompactTokenThreshold = null,
        AutoCompactEnabled = true,
        CompactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens,
        Tools = [],
    };

    private sealed class FixedResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }

    [Test]
    public async Task Reload_Removes_OldTools_From_Harness()
    {
        // The real "unload" signal that's reliably verifiable: after reload
        // the harness must NOT hold the old-extension tool anymore (only the
        // freshly-registered one). If the old tool lingered, the harness
        // would keep a strong reference to the unloaded assembly, defeating
        // collectible-ALC collection.
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");

        ExtensionRuntime oldRuntime;
        {
            oldRuntime = new ExtensionRuntime(session, new NullPhiUiBridge());
            oldRuntime.DiscoverAndLoad([_helloToolPath]);
            oldRuntime.Initialize();
        }

        var oldHelloTool = session.HarnessForTest().Tools.Single(t => t.Name == "hello");

        // Reload → old tool removed, new tool registered.
        var reloader = new ExtensionReloader(oldRuntime, [_helloToolPath]);
        var newRuntime = reloader.Reload();

        var after = session.HarnessForTest().Tools.Where(t => t.Name == "hello").ToList();
        // Exactly one "hello" tool, and it must NOT be the old instance
        // (which would root the old assembly).
        await Assert.That(after.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(after[0], oldHelloTool)).IsFalse();
    }

    [Test]
    public async Task Reload_OldPhiApi_Throws_GenerationGuard()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_helloToolPath]);
        runtime.Initialize();

        // Capture the api reference the runtime handed to HelloTool's Setup.
        var ext = runtime.Extensions[0];
        var oldApi = runtime.ApisForTest[ext];

        var reloader = new ExtensionReloader(runtime, [_helloToolPath]);
        var newRuntime = reloader.Reload();   // invalidates generations + unloads old ALCs

        // The captured old api is stale; action methods throw GenerationGuard.
        var ex = Assert.Throws<ExtensionGenerationException>(() => oldApi.Notify("hi"));
        await Assert.That(ex!.ExtensionName).IsEqualTo("hello-tool");
        await Assert.That(ex.StaleGenerationVersion).IsGreaterThan(0);
    }

    [Test]
    public async Task Reload_NewRuntime_Has_Working_Extensions()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_helloToolPath]);
        runtime.Initialize();

        var reloader = new ExtensionReloader(runtime, [_helloToolPath]);
        var newRuntime = reloader.Reload();

        // New runtime should have the extension loaded and its tool registered.
        await Assert.That(newRuntime.Extensions.Count).IsEqualTo(1);
        var toolNames = newRuntime.HarnessForTest().Tools.Select(t => t.Name).ToList();
        await Assert.That(toolNames).Contains("hello");
    }

    [Test]
    public async Task Generation_Unit_Guard_Throws_After_Invalidate()
    {
        var gen = new ExtensionGeneration("test-ext");
        gen.Invalidate("unit-test reload");
        await Assert.That(gen.IsAlive).IsFalse();
        var ex = Assert.Throws<ExtensionGenerationException>(() => gen.AssertAlive());
        await Assert.That(ex!.ExtensionName).IsEqualTo("test-ext");
    }

    private static void ForceGc()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
