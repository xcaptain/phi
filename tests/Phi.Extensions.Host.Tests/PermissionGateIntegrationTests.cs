using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// End-to-end: load the built PermissionGate dll, verify it registers a
/// <c>tool_call</c> hook, and that the hook blocks a dangerous bash command
/// while letting safe commands through. Proves the interception pipeline
/// works against a real extension (Sprint 2's demo).
/// </summary>
[NotInParallel("permission-gate")]
public class PermissionGateIntegrationTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _gatePath;

    public PermissionGateIntegrationTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Phi.Extensions.PermissionGate.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples", "extensions", "PermissionGate", "bin", "Debug", "net10.0", "Phi.Extensions.PermissionGate.dll"),
        };
        _gatePath = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("PermissionGate.dll not found", candidates[0]);
    }

    public void Dispose()
    {
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
    public async Task Load_Discovers_PermissionGate_Attribute()
    {
        var loaded = ExtensionLoader.Load(_gatePath);
        await Assert.That(loaded.Name).IsEqualTo("permission-gate");
        await Assert.That(loaded.Instance).IsAssignableTo<IPhiExtension>();
    }

    [Test]
    public async Task Dangerous_Bash_Command_Is_Blocked_By_Hook()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad(new[] { _gatePath });
        runtime.Initialize();

        await Assert.That(runtime.SetupResults.Count).IsEqualTo(0);

        // Build a "bash" tool that records it ran, then wrap it like the
        // extension runtime would, and execute a dangerous command.
        var recorder = new RecordingBashTool();
        var wrapped = new HookWrappingTool(recorder, new HookRegistryTestHack(runtime).Registry);
        var result = await wrapped.ExecuteAsync("bash", "call-1",
            new JsonObject { ["command"] = "rm -rf /important" }, default);

        // Blocked: the inner tool never ran.
        await Assert.That(recorder.Ran).IsFalse();
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("guarded pattern");
    }

    [Test]
    public async Task Safe_Bash_Command_Is_Allowed()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad(new[] { _gatePath });
        runtime.Initialize();

        var recorder = new RecordingBashTool();
        var wrapped = new HookWrappingTool(recorder, new HookRegistryTestHack(runtime).Registry);
        var result = await wrapped.ExecuteAsync("bash", "call-2",
            new JsonObject { ["command"] = "ls -la" }, default);

        await Assert.That(recorder.Ran).IsTrue();
        await Assert.That(result.IsError).IsFalse();
    }

    /// <summary>A bash tool that records whether it executed (real execution would need a shell).</summary>
    private sealed class RecordingBashTool : Tool
    {
        public bool Ran { get; private set; }
        public override string Name => "bash";
        public override string Description => "run a shell command";
        public override JsonObject Parameters => new() { ["type"] = "object" };

        public override Task<ToolResult> ExecuteAsync(
            string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
        {
            Ran = true;
            return Task.FromResult(new ToolResult([new TextBlock("ok")]));
        }
    }

    /// <summary>Exposes the runtime's HookRegistry to tests so we can wrap a
    /// tool with the SAME registry the PermissionGate hooks were registered
    /// into.</summary>
    private sealed class HookRegistryTestHack(ExtensionRuntime runtime)
    {
        public HookRegistry Registry => runtime.GetHookRegistryForTest();
    }
}

/// <summary>Test seam: exposes the internal HookRegistry for wrapping a tool with the live hooks.</summary>
internal static class ExtensionRuntimeHookAccessor
{
    public static HookRegistry GetHookRegistryForTest(this ExtensionRuntime runtime) =>
        runtime.GetType()
            .GetField("_hooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(runtime) as HookRegistry
        ?? throw new InvalidOperationException("hooks not initialized");
}
