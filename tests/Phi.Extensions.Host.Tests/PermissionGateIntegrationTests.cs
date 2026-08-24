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
        runtime.DiscoverAndLoad([_gatePath]);
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
        runtime.DiscoverAndLoad([_gatePath]);
        runtime.Initialize();

        var recorder = new RecordingBashTool();
        var wrapped = new HookWrappingTool(recorder, new HookRegistryTestHack(runtime).Registry);
        var result = await wrapped.ExecuteAsync("bash", "call-2",
            new JsonObject { ["command"] = "ls -la" }, default);

        await Assert.That(recorder.Ran).IsTrue();
        await Assert.That(result.IsError).IsFalse();
    }

    [Test]
    public async Task Dangerous_Bash_Blocks_When_UserDenies_InUiBridge()
    {
        // Sprint 3: with a real PhiUiBridge that asks the user, denying
        // produces the same blocked result as the headless fallback (and
        // the inner tool still doesn't run). The user sees the prompt +
        // a follow-up Notify ("Blocked guarded command…").
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = true;
        var uiSink = new StubUiSink
        {
            HasUi = true,
            OnConfirm = (_, _, _) => Task.FromResult(false), // user denies
        };
        var bridge = new PhiUiBridge(uiSink);
        using var runtime = new ExtensionRuntime(session, bridge);
        runtime.DiscoverAndLoad([_gatePath]);
        runtime.Initialize();

        var recorder = new RecordingBashTool();
        var wrapped = new HookWrappingTool(recorder, new HookRegistryTestHack(runtime).Registry);
        var result = await wrapped.ExecuteAsync("bash", "call-3",
            new JsonObject { ["command"] = "rm -rf /important" }, default);

        await Assert.That(recorder.Ran).IsFalse();
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(uiSink.ConfirmCalls).Count().IsEqualTo(1);
        // The "Blocked…" Notify is the user-facing feedback after denial.
        await Assert.That(uiSink.Notifies.Any(n => n.Message.Contains("Blocked"))).IsTrue();
    }

    [Test]
    public async Task Dangerous_Bash_Allowed_When_UserApproves_InUiBridge()
    {
        // Sprint 3: with the user explicitly approving, the dangerous
        // command runs. The hook lets it through and emits a "Permission
        // granted" Notify for transparency. Inner tool records it ran.
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = true;
        var uiSink = new StubUiSink
        {
            HasUi = true,
            OnConfirm = (_, _, _) => Task.FromResult(true), // user approves
        };
        var bridge = new PhiUiBridge(uiSink);
        using var runtime = new ExtensionRuntime(session, bridge);
        runtime.DiscoverAndLoad([_gatePath]);
        runtime.Initialize();

        var recorder = new RecordingBashTool();
        var wrapped = new HookWrappingTool(recorder, new HookRegistryTestHack(runtime).Registry);
        var result = await wrapped.ExecuteAsync("bash", "call-4",
            new JsonObject { ["command"] = "git push --force origin main" }, default);

        await Assert.That(recorder.Ran).IsTrue();
        await Assert.That(result.IsError).IsFalse();
        await Assert.That(uiSink.ConfirmCalls).Count().IsEqualTo(1);
        await Assert.That(uiSink.Notifies.Any(n => n.Message.Contains("granted"))).IsTrue();
    }

    [Test]
    public async Task HasUiFalse_Falls_Back_To_Headless_AutoBlock()
    {
        // Sprint 3: when the host has no UI, ConfirmAsync returns false
        // (NullUiSink default), so the gate behaves exactly like Sprint 2:
        // auto-block with no user interaction. This is the regression-safety
        // contract — CI / automation / unit tests never see a confirm dialog.
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_gatePath]);
        runtime.Initialize();

        var recorder = new RecordingBashTool();
        var wrapped = new HookWrappingTool(recorder, new HookRegistryTestHack(runtime).Registry);
        var result = await wrapped.ExecuteAsync("bash", "call-5",
            new JsonObject { ["command"] = "rm -rf /important" }, default);

        await Assert.That(recorder.Ran).IsFalse();
        await Assert.That(result.IsError).IsTrue();
    }

    /// <summary>
    /// Recording <see cref="IUiSink"/> for Sprint 3 tests: captures
    /// notifications + confirm calls so the test can assert the dialog
    /// was shown and the post-decision Notify fired.
    /// </summary>
    private sealed class StubUiSink : IUiSink
    {
        public bool HasUi { get; set; }
        public List<(string Message, NotifyLevel Level)> Notifies { get; } = [];
        public List<(string Title, string Message)> ConfirmCalls { get; } = [];
        public Func<string, string, TimeSpan?, Task<bool>>? OnConfirm { get; set; }

        public void Notify(string message, NotifyLevel level) => Notifies.Add((message, level));
        public void NotifyStatus(string message) { }
        public void FlashError(string message, bool persistent) { }
        public void SubmitTranscriptLine(Phi.Extensions.TranscriptLine line) { }
        public Task<string?> ShowSelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
            => Task.FromResult<string?>(null);
        public Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout)
        {
            ConfirmCalls.Add((title, message));
            return OnConfirm?.Invoke(title, message, timeout) ?? Task.FromResult(false);
        }
        public Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout)
            => Task.FromResult<string?>(null);
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
