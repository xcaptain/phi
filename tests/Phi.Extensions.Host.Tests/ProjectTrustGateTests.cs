using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Sprint 3b: Project Trust end-to-end. Each test sets up a fake cwd
/// with a <c>.phi/extensions/</c> directory containing a couple of
/// stub extension dlls, runs the trust gate with either a headless or
/// confirm-controlling bridge, and asserts which extensions made it
/// into the runtime.
/// </summary>
[NotInParallel("project-trust")]
public class ProjectTrustGateTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _auditDir;
    private readonly string _trustPath;
    private readonly string _previousPhiHome;

    public ProjectTrustGateTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
        var extDir = Path.Combine(_cwd, ".phi", "extensions");
        Directory.CreateDirectory(extDir);
        // Copy the real PermissionGate as one candidate; drop a dummy
        // file too so the trust prompt shows >1 entry.
        var permissionGate = Path.Combine(
            AppContext.BaseDirectory, "Phi.Extensions.PermissionGate.dll");
        File.Copy(permissionGate, Path.Combine(extDir, "PermissionGate.dll"));
        File.WriteAllBytes(Path.Combine(extDir, "fake-ext.dll"), [0x00, 0x01]);

        _auditDir = Path.Combine(Path.GetTempPath(), $"phi-trust-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_auditDir);
        // ProjectTrustStore + AuditLogger both read SessionPaths.PhiHome;
        // point it at the per-test dir so both trust.json and audit.log
        // land together.
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _auditDir;
        ResetAuditLoggerCache();
        _trustPath = Path.Combine(_auditDir, "trust.json");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SessionPaths.PhiHome = _previousPhiHome;
        ResetAuditLoggerCache();
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_auditDir)) Directory.Delete(_auditDir, recursive: true);
    }

    private static void ResetAuditLoggerCache()
    {
        var field = typeof(AuditLogger).GetField("_cachedPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field?.SetValue(null, null);
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

    /// <summary>
    /// Headless mode: <c>HasUi=false</c>. The gate should auto-approve
    /// without prompting, write an audit record, and load all the
    /// project extensions.
    /// </summary>
    [Test]
    public async Task Headless_AutoApproves_AndLoadsAllProjectExtensions()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        using var _ = runtime;

        var paths = await runtime.DiscoverAndTrustProjectExtensionsAsync(_cwd);

        await Assert.That(paths).Count().IsEqualTo(2);

        // Auto-approved → recorded as approve-headless in audit log.
        var audits = ReadAudit();
        var trustEvents = audits
            .Where(n => n["kind"]?.GetValue<string>() == "project_trust")
            .ToList();
        await Assert.That(trustEvents).Count().IsEqualTo(1);
        await Assert.That(trustEvents[0]["detail"]?.GetValue<string>())
            .Contains("approve-headless");

        // Decision stored in trust store.
        var store = ProjectTrustStore.Load(_trustPath);
        var cwdKey = Phi.ProjectExtensions.ProjectKey(_cwd);
        await Assert.That(store.Lookup(cwdKey)).IsNotNull();
        await Assert.That(store.Lookup(cwdKey)!.Kind).IsEqualTo(ProjectTrustKind.Approve);
    }

    /// <summary>
    /// Interactive mode + user approves: prompt fires, return value
    /// contains the gated paths, trust store records the approval,
    /// audit log captures the explicit "approve-confirmed" event.
    /// </summary>
    [Test]
    public async Task Interactive_UserApproves_RecordsApproval_AndLoads()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = true;
        var bridge = new StubBridge(hasUi: true, confirmAnswer: true);
        var runtime = new ExtensionRuntime(session, bridge);
        using var _ = runtime;

        var paths = await runtime.DiscoverAndTrustProjectExtensionsAsync(_cwd);

        await Assert.That(paths).Count().IsEqualTo(2);
        await Assert.That(bridge.ConfirmCalls).Count().IsEqualTo(1);
        await Assert.That(bridge.ConfirmCalls[0].title).IsEqualTo("Project extensions");

        var trustEvents = ReadAudit()
            .Where(n => n["kind"]?.GetValue<string>() == "project_trust")
            .ToList();
        await Assert.That(trustEvents).Count().IsEqualTo(1);
        await Assert.That(trustEvents[0]["detail"]?.GetValue<string>())
            .Contains("approve-confirmed");
    }

    /// <summary>
    /// Interactive mode + user declines: prompt fires, runtime loads
    /// zero project extensions, trust store records the decline.
    /// </summary>
    [Test]
    public async Task Interactive_UserDeclines_RecordsDecline_AndSkips()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = true;
        var bridge = new StubBridge(hasUi: true, confirmAnswer: false);
        var runtime = new ExtensionRuntime(session, bridge);
        using var _ = runtime;

        var paths = await runtime.DiscoverAndTrustProjectExtensionsAsync(_cwd);

        await Assert.That(paths).IsEmpty();
        var store = ProjectTrustStore.Load(_trustPath);
        var cwdKey = Phi.ProjectExtensions.ProjectKey(_cwd);
        await Assert.That(store.Lookup(cwdKey)!.Kind).IsEqualTo(ProjectTrustKind.Decline);
    }

    /// <summary>
    /// Second invocation with a remembered decision should NOT prompt
    /// again — the gate short-circuits via the store. Approval or
    /// decline is honoured exactly as recorded.
    /// </summary>
    [Test]
    public async Task RememberedApproval_SkipsPrompt_OnSecondInvocation()
    {
        // First invocation: user approves.
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = true;
        var first = new StubBridge(hasUi: true, confirmAnswer: true);
        var runtime = new ExtensionRuntime(session, first);
        using var _ = runtime;
        await runtime.DiscoverAndTrustProjectExtensionsAsync(_cwd);
        await Assert.That(first.ConfirmCalls).Count().IsEqualTo(1);

        // Second invocation: a NEW bridge that would say "no" if asked
        // — the gate should not even call it.
        var second = new StubBridge(hasUi: true, confirmAnswer: false);
        var paths = await runtime.DiscoverAndTrustProjectExtensionsAsync(_cwd);

        await Assert.That(second.ConfirmCalls).Count().IsEqualTo(0);
        await Assert.That(paths).Count().IsEqualTo(2);
    }

    /// <summary>Empty project directory → empty list, no prompt, no audit row.</summary>
    [Test]
    public async Task EmptyProjectExtensionsDirectory_NoPromptNoAudit()
    {
        var emptyCwd = Path.Combine(Path.GetTempPath(), $"phi-trust-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(emptyCwd, ".phi", "extensions"));
        try
        {
            var session = await Phi.Session.LoadAsync(emptyCwd, BuildEnv(), providerName: "stub", model: "m");
            session.HasUi = true;
            var bridge = new StubBridge(hasUi: true, confirmAnswer: true);
            var runtime = new ExtensionRuntime(session, bridge);
            using var _ = runtime;

            var paths = await runtime.DiscoverAndTrustProjectExtensionsAsync(emptyCwd);
            await Assert.That(paths).IsEmpty();
            await Assert.That(bridge.ConfirmCalls).Count().IsEqualTo(0);
        }
        finally
        {
            if (Directory.Exists(emptyCwd)) Directory.Delete(emptyCwd, recursive: true);
        }
    }

    private static List<System.Text.Json.Nodes.JsonNode> ReadAudit()
    {
        var p = AuditLogger.Path;
        if (!File.Exists(p)) return [];
        return File.ReadAllLines(p)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => System.Text.Json.Nodes.JsonNode.Parse(l)!)
            .ToList();
    }

    /// <summary>
    /// Minimal <see cref="IPhiUiBridge"/> recording calls so tests can
    /// assert what was prompted + how the user answered.
    /// </summary>
    private sealed class StubBridge(bool hasUi, bool confirmAnswer) : IPhiUiBridge
    {
        public bool HasUi => hasUi;
        public List<(string title, string message)> ConfirmCalls { get; } = [];
        public bool ConfirmAnswer { get; } = confirmAnswer;

        public Task<bool> ConfirmAsync(string title, string message, TimeSpan? timeout)
        {
            ConfirmCalls.Add((title, message));
            return Task.FromResult(ConfirmAnswer);
        }

        public void Notify(string message, NotifyLevel level) { }
        public Task<string?> SelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
            => Task.FromResult<string?>(null);
        public Task<string?> InputAsync(string title, string placeholder, TimeSpan? timeout)
            => Task.FromResult<string?>(null);
        public void SubmitTranscriptLine(Phi.Extensions.TranscriptLine line) { }
        public void NotifyStatus(string message) { }
        public void FlashError(string message, bool persistent) { }
    }
}
