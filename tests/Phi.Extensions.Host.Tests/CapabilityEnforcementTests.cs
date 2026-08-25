using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Sprint 3b: capability enforcement tests. Each <see cref="IPhiApi"/>
/// action method in <see cref="CapabilityActionMap"/> is gated against
/// the extension's declared <see cref="PhiExtensionAttribute.Capabilities"/>;
/// the runtime either logs (v1 transparent) or throws (v1.5 strict).
/// </summary>
[NotInParallel("capability-enforcement")]
public class CapabilityEnforcementTests : IDisposable
{
    private readonly string _auditDir;
    private readonly string _previousPhiHome;

    public CapabilityEnforcementTests()
    {
        // Isolate the audit log per test so assertions can read it back
        // without cross-test interference. AuditLogger reads SessionPaths.PhiHome
        // (which sessions + trust store + DeskLog also use) and caches the
        // resolved path; each test points PhiHome at a fresh temp dir and
        // resets the cache so the cache lands on the new path.
        _auditDir = Path.Combine(Path.GetTempPath(), $"phi-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_auditDir);
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _auditDir;
        ResetAuditLoggerCache();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SessionPaths.PhiHome = _previousPhiHome;
        ResetAuditLoggerCache();
        if (Directory.Exists(_auditDir)) Directory.Delete(_auditDir, recursive: true);
    }

    /// <summary>Test seam: AuditLogger caches the path on first access.</summary>
    private static void ResetAuditLoggerCache()
    {
        var field = typeof(AuditLogger).GetField("_cachedPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field?.SetValue(null, null);
    }

    /// <summary>Read the audit log lines back as parsed JSON nodes.</summary>
    private static List<JsonNode> ReadAudit()
    {
        if (!File.Exists(AuditLogger.Path)) return [];
        return File.ReadAllLines(AuditLogger.Path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonNode.Parse(l)!)
            .ToList();
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
    /// Fake extension that captures the <see cref="IPhiApi"/> it received
    /// during <c>Setup</c>, then exposes the captured reference to the
    /// test so it can drive action methods after the runtime is fully
    /// initialised (matching how a real extension would hold an api
    /// reference after registration). Declares no capabilities, so the
    /// tests can drive a "missing capability" path.
    /// </summary>
    [PhiExtension(
        Name = "cap-test-ext",
        Version = "1.0.0",
        Description = "Test extension capturing its IPhiApi.",
        Capabilities = ExtensionCapability.None)]
    private sealed class CapturingExtension(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api) => onSetup(api);
    }

    /// <summary>
    /// Build a runtime with a single in-memory extension whose declared
    /// capabilities are <paramref name="declared"/>. The extension
    /// captures its <see cref="IPhiApi"/> so the test can call
    /// action methods on it after <c>Initialize</c> returns.
    /// </summary>
    private static async Task<(Phi.Session Session, ExtensionRuntime Runtime, IPhiApi Api)>
        BuildAsync(ExtensionCapability declared)
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"phi-cap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var session = await Phi.Session.LoadAsync(cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());

        IPhiApi? captured = null;
        // Declared capability flows through the [PhiExtension] attribute on
        // the helper class. Passing it through ctor means we need a
        // distinct subclass per capability set — easier to use the
        // two dedicated helpers below (NoneCap / DeclaredCap).
        _ = declared;
        runtime.RegisterCompiledExtension(new CapturingExtension(api => captured = api));

        runtime.Initialize();
        return (session, runtime, captured!);
    }

    [Test]
    public async Task TransparentMode_MissingCapability_LogsAudit_AndProceeds()
    {
        // CapturingExtension declares Capabilities = None (default), so
        // Notify is undeclared. Transparent mode logs + proceeds.
        var (session, runtime, api) = await BuildAsync(ExtensionCapability.None);
        using var _ = runtime;

        await Assert.That(api).IsNotNull();

        // Should NOT throw. Notifying a NullPhiUiBridge is a no-op anyway,
        // but the key assertion is that the capability gate allowed it.
        api.Notify("hello");

        // Audit log records the mismatch.
        var mismatches = ReadAudit()
            .Where(n => n["kind"]?.GetValue<string>() == "capability_mismatch")
            .ToList();
        await Assert.That(mismatches).IsNotEmpty();
        await Assert.That(mismatches[0]["method"]?.GetValue<string>()).IsEqualTo("Notify");
        await Assert.That(mismatches[0]["extension"]?.GetValue<string>()).IsEqualTo("cap-test-ext");
    }

    [Test]
    public async Task StrictMode_MissingCapability_ThrowsExtensionError()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"phi-cap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var session = await Phi.Session.LoadAsync(cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.CapabilityEnforcement = CapabilityEnforcementMode.Strict;

        IPhiApi? captured = null;
        runtime.RegisterCompiledExtension(new CapturingExtension(api => captured = api));
        runtime.Initialize();
        using var _ = runtime;

        await Assert.That(captured).IsNotNull();

        // Notify requires UiInteract; CapturingExtension declared None —
        // strict mode throws ExtensionError.
        var ex = await Assert.ThrowsAsync<ExtensionError>(async () =>
        {
            captured!.Notify("hello");
            await Task.CompletedTask;
        });
        await Assert.That(ex!.Message).Contains("Notify");
        await Assert.That(ex.Message).Contains("UiInteract");

        // Audit log records the blocked attempt too.
        var blocks = ReadAudit()
            .Where(n => n["kind"]?.GetValue<string>() == "capability_blocked")
            .ToList();
        await Assert.That(blocks).IsNotEmpty();
    }

    [Test]
    public async Task DeclaredCapability_AllowsAction_BothModes()
    {
        // DeclaringExtension declares UiInteract + TranscriptWrite;
        // both Notify and SubmitUserMessage should succeed under strict
        // enforcement. The transparent-mode assertion is implicit — if
        // strict lets the call through, transparent will too.
        var cwd = Path.Combine(Path.GetTempPath(), $"phi-cap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var session = await Phi.Session.LoadAsync(cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.CapabilityEnforcement = CapabilityEnforcementMode.Strict;

        IPhiApi? captured = null;
        runtime.RegisterCompiledExtension(new DeclaringExtension(api => captured = api));
        runtime.Initialize();
        using var _ = runtime;

        // Notify → declared, no throw.
        captured!.Notify("ok");
        // SubmitUserMessage → declared, no throw.
        captured.SubmitUserMessage("hi");

        // No capability_mismatch / capability_blocked lines for this ext.
        var issues = ReadAudit()
            .Where(n =>
            {
                var kind = n["kind"]?.GetValue<string>();
                return kind is "capability_mismatch" or "capability_blocked";
            })
            .Where(n => n["extension"]?.GetValue<string>() == "declaring-ext")
            .ToList();
        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ExtensionLoaded_And_SetupOk_Written_To_Audit()
    {
        var (session, runtime, _) = await BuildAsync(ExtensionCapability.None);
        using var _ = runtime;

        var kinds = ReadAudit()
            .Select(n => n["kind"]?.GetValue<string>())
            .Where(k => k != null)
            .Cast<string>()
            .ToHashSet();
        await Assert.That(kinds).Contains("extension_loaded");
        await Assert.That(kinds).Contains("extension_setup_ok");
    }

    /// <summary>
    /// Like <see cref="CapturingExtension"/> but with explicit
    /// UiInteract + TranscriptWrite capabilities, so tests can assert
    /// "declared capability allows the action under strict mode".
    /// </summary>
    [PhiExtension(
        Name = "declaring-ext",
        Version = "1.0.0",
        Description = "Test extension with declared capabilities.",
        Capabilities = ExtensionCapability.UiInteract | ExtensionCapability.TranscriptWrite)]
    private sealed class DeclaringExtension(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api) => onSetup(api);
    }
}
