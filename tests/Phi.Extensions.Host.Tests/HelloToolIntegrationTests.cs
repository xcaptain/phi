using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// End-to-end: load the built HelloTool dll, run Setup, verify the
/// registered tool is invocable through the harness and its result
/// appears in the session transcript.
/// <para>
/// This is the test that proves "extension 端到端 works" — the Sprint 1
/// exit criterion.
/// </para>
/// </summary>
[NotInParallel("hello-tool")]
public class HelloToolIntegrationTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _helloToolPath;

    public HelloToolIntegrationTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-hello-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);

        // HelloTool.dll is produced by extensions/HelloTool and
        // copied to the test artifacts dir. Locate it relative to the test
        // bin directory.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Phi.Extensions.HelloTool.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples", "extensions", "HelloTool", "bin", "Debug", "net10.0", "Phi.Extensions.HelloTool.dll"),
        };
        _helloToolPath = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "HelloTool.dll not found. Build extensions/HelloTool first.",
                candidates[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
    }

    /// <summary>
    /// Build a minimal <see cref="SessionEnvironment"/> for tests: null
    /// provider, default compaction, no system prompt. Inlined here (not
    /// imported from <c>Phi.Tests.Helpers</c>) to keep the host test
    /// project free of internal-helper dependencies.
    /// </summary>
    private static SessionEnvironment BuildEnv() => new()
    {
        ProviderResolver = new FixedProviderResolver(new NullProvider()),
        SystemPrompt = new SystemPromptOptions { ResolvedSystemPrompt = "stub prompt" },
        MaxTurns = 5,
        ContextWindowTokens = ContextWindow.DefaultContextWindowTokens,
        AutoCompactTokenThreshold = null,
        AutoCompactEnabled = true,
        CompactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens,
        Tools = [],
    };

    /// <summary>Tiny inline resolver so we can pick a provider name in LoadAsync.</summary>
    private sealed class FixedProviderResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }

    [Test]
    public async Task Load_Instantiates_HelloTool_And_Discovers_Attribute()
    {
        var loaded = ExtensionLoader.Load(_helloToolPath);

        await Assert.That(loaded.Name).IsEqualTo("hello-tool");
        await Assert.That(loaded.Version).IsEqualTo("1.0.0");
        await Assert.That(loaded.Description).IsEqualTo("Greet someone by name.");
        await Assert.That(loaded.EntryType.Name).IsEqualTo("HelloToolExt");
        await Assert.That(loaded.Instance).IsAssignableTo<IPhiExtension>();
    }

    [Test]
    public async Task Runtime_Initialize_Calls_Setup_And_Tool_Is_Invocable()
    {
        var session = await BuildHeadlessSession();
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_helloToolPath]);
        runtime.Initialize();

        // Setup() ran without throwing (SetupResults empty).
        await Assert.That(runtime.SetupResults.Count).IsEqualTo(0);

        // HelloTool registered "/hello" — it should be in the command registry.
        await Assert.That(runtime.Commands.Keys).Contains("/hello");

        // ──────── Tool invocation (the "tool call 可用" criterion) ────────
        var tool = session.HarnessForTest().Tools.Single(t => t.Name == "hello");
        var args = System.Text.Json.Nodes.JsonNode.Parse("""{"who":"Phi"}""")!.AsObject();
        var result = await tool.ExecuteAsync("hello", "test-call-1", args, default);

        await Assert.That(result.IsError).IsFalse();
        var text = (TextBlock)result.Content[0];
        await Assert.That(text.Text).IsEqualTo("Hello, Phi!");
    }

    [Test]
    public async Task Tool_With_Missing_Who_Defaults_To_World()
    {
        var session = await BuildHeadlessSession();
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_helloToolPath]);
        runtime.Initialize();

        var tool = session.HarnessForTest().Tools.Single(t => t.Name == "hello");
        var result = await tool.ExecuteAsync("hello", "call-2",
            System.Text.Json.Nodes.JsonNode.Parse("""{}""")!.AsObject(), default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(((TextBlock)result.Content[0]).Text).IsEqualTo("Hello, world!");
    }

    [Test]
    public async Task AddPromptGuideline_Updates_State_SystemPrompt()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(),
            providerName: "stub", model: "hello-model");
        var beforePrompt = session.State.SystemPrompt;

        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_helloToolPath]);
        runtime.Initialize();

        // The guideline HelloTool added should appear in State.SystemPrompt.
        await Assert.That(session.State.SystemPrompt).Contains("greet them back with hello");
        await Assert.That(session.State.SystemPrompt.Length).IsGreaterThan(beforePrompt.Length);
    }

    [Test]
    public async Task Tool_Registration_Is_Picked_Up_By_Harness_Tool_List()
    {
        // Sprint 1 acceptance: after Initialize(), the tool registered by
        // HelloTool is in the live harness's tool list (the model would see
        // it on the next turn).
        var session = await BuildHeadlessSession();
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_helloToolPath]);
        runtime.Initialize();

        var names = session.HarnessForTest().Tools.Select(t => t.Name).ToList();
        await Assert.That(names).Contains("hello");
    }

    [Test]
    public async Task Load_Failure_Path_Is_Recorded_Not_Thrown()
    {
        // DiscoverAndLoad swallows per-file load errors into LoadResults so
        // one bad dll doesn't brick the host.
        var goodPath = _helloToolPath;
        var badPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.dll");

        var session = await BuildHeadlessSession();
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([goodPath, badPath]);
        runtime.Initialize();

        await Assert.That(runtime.Extensions.Count).IsEqualTo(1);
        await Assert.That(runtime.Extensions[0].Name).IsEqualTo("hello-tool");
        await Assert.That(runtime.LoadResults.Count).IsEqualTo(1);
        await Assert.That(runtime.LoadResults[0].AssemblyPath).IsEqualTo(badPath);
    }

    private async Task<Phi.Session> BuildHeadlessSession()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(),
            providerName: "stub", model: "hello-model");
        session.HasUi = false;   // headless test
        return session;
    }
}

/// <summary>
/// Exposes the internal <see cref="Phi.Agent.Harness"/> from a
/// <see cref="Phi.Session"/> for test assertions. In production code the
/// harness is private; tests need to peek into it to verify tool registration
/// and execution paths without going through the LLM.
/// </summary>
internal static class SessionTestExtensions
{
    public static Phi.Agent.Harness HarnessForTest(this Phi.Session session) =>
        session.GetType()
            .GetField("_harness", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session) as Phi.Agent.Harness
        ?? throw new InvalidOperationException("harness not initialized");
}
