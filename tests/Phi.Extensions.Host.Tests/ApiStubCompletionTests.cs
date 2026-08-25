using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Extensions.Rendering;
using Phi.Prompts;
using Phi.Provider;
using Phi.Providers;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Sprint 4.5: the previously-stubbed <see cref="IPhiApi"/> actions now work
/// end to end — <c>SwitchModel</c> / <c>SwitchProvider</c> (delegate to the
/// session), <c>AppendEntryAsync</c> (namespaced persistence), and
/// <c>SubmitCustomMessage</c> + <c>RegisterMessageRenderer</c> (custom-typed
/// assistant messages persisted + rendered).
/// </summary>
[NotInParallel("api-stubs")]
public class ApiStubCompletionTests : IDisposable
{
    private readonly string _cwd;

    public ApiStubCompletionTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-stubs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
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

    /// <summary>Captures the api; registers a message renderer for "ext:notice".</summary>
    [PhiExtension(
        Name = "stub-ext",
        Version = "1.0.0",
        Description = "Exercises the 5 completed IPhiApi actions.",
        Capabilities = ExtensionCapability.TranscriptWrite | ExtensionCapability.UiInteract)]
    private sealed class StubExt(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api)
        {
            api.RegisterMessageRenderer("ext:notice", (contentType, content, details) =>
            {
                var level = details?.TryGetValue("level", out var l) == true
                    ? l?.ToString() ?? "info"
                    : "info";
                return $"[{level}] {content}";
            });
            onSetup(api);
        }
    }

    private async Task<(Phi.Session Session, ExtensionRuntime Runtime, IPhiApi Api)> BuildAsync()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        IPhiApi? captured = null;
        runtime.RegisterCompiledExtension(new StubExt(api => captured = api));
        runtime.Initialize();
        return (session, runtime, captured!);
    }

    // ──────── AppendEntryAsync ────────

    [Test]
    public async Task AppendEntryAsync_PersistsNamespacedEntry_AndSkipsResumeMessages()
    {
        var (session, runtime, api) = await BuildAsync();
        using var _ = runtime;

        await api.AppendEntryAsync("ext:state", new Dictionary<string, object?>
        {
            ["count"] = 42,
            ["name"] = "phi",
            ["nested"] = new Dictionary<string, object?> { ["flag"] = true },
        });

        // The entry lands in the JSONL as kind "extension". Property names
        // are PascalCase (PhiAgentJsonContext has no naming policy); the
        // dict payload keys are verbatim.
        var raw = File.ReadAllText(runtime.StorageForTest().FilePath);
        var node = JsonNode.Parse(raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0])!.AsObject();
        await Assert.That(node["kind"]!.GetValue<string>()).IsEqualTo("extension");
        await Assert.That(node["Namespace"]!.GetValue<string>()).IsEqualTo("ext:state");
        await Assert.That(node["Data"]!["count"]!.GetValue<int>()).IsEqualTo(42);
        await Assert.That(node["Data"]!["nested"]!["flag"]!.GetValue<bool>()).IsTrue();

        // It does NOT surface as a conversation message (LoadMessages skips it).
        await Assert.That(session.LoadMessages()).IsEmpty();

        // Resume replays it: rebuild from disk, no crash, still not a message.
        var resumed = Phi.Session.Resume(session.Id, _cwd);
        await Assert.That(resumed.LoadMessages()).IsEmpty();
    }

    [Test]
    public async Task AppendEntryAsync_UnsupportedValueType_Throws()
    {
        var (_, runtime, api) = await BuildAsync();
        using var _ = runtime;

        // A random object can't be serialized AOT-safely; the converter must
        // reject it with a helpful message rather than silently reflection.
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            api.AppendEntryAsync("ext:bad", new Dictionary<string, object?>
            {
                ["weird"] = new object(),
            }));
        await Assert.That(ex!.Message).Contains("not serializable");
    }

    // ──────── SubmitCustomMessage + RegisterMessageRenderer ────────

    [Test]
    public async Task SubmitCustomMessage_Persists_InjectsIntoHarness_AndRegistersRenderer()
    {
        var (session, runtime, api) = await BuildAsync();
        using var _ = runtime;

        api.SubmitCustomMessage(
            "hello there",
            "ext:notice",
            details: new Dictionary<string, object?> { ["level"] = "warn" },
            triggerTurn: false);

        // Persisted as kind "custom" (PascalCase property names).
        var raw = File.ReadAllText(runtime.StorageForTest().FilePath);
        var entry = JsonNode.Parse(raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0])!.AsObject();
        await Assert.That(entry["kind"]!.GetValue<string>()).IsEqualTo("custom");
        await Assert.That(entry["CustomType"]!.GetValue<string>()).IsEqualTo("ext:notice");
        await Assert.That(entry["Content"]!.GetValue<string>()).IsEqualTo("hello there");

        // Injected into the harness so the model sees it next turn.
        var harnessMessages = runtime.HarnessForTest().Messages;
        await Assert.That(harnessMessages).Count().IsEqualTo(1);
        await Assert.That(harnessMessages[0]).IsTypeOf<CustomMessage>();
        var cm = (CustomMessage)harnessMessages[0];
        await Assert.That(cm.CustomType).IsEqualTo("ext:notice");

        // Renderer is queryable and produces the expected fragment.
        var hasRenderer = runtime.TryGetMessageRenderer("ext:notice", out var renderer);
        await Assert.That(hasRenderer).IsTrue();
        var fragment = ((MessageRenderer)renderer!)(cm.CustomType, cm.Text,
            new Dictionary<string, object?> { ["level"] = "warn" });
        await Assert.That(fragment).IsEqualTo("[warn] hello there");
    }

    [Test]
    public async Task SubmitCustomMessage_WithTriggerTurn_EnqueuesFollowUp()
    {
        var (session, runtime, api) = await BuildAsync();
        using var _ = runtime;

        api.SubmitCustomMessage("hi", "ext:notice", triggerTurn: true);

        // The follow-up queue should have one message waiting to run the agent.
        await Assert.That(session.State.FollowUpCount).IsEqualTo(1);
    }

    // ──────── SwitchModel / SwitchProvider ────────

    [Test]
    public async Task SwitchModel_Delegates_ToSession()
    {
        var (session, runtime, api) = await BuildAsync();
        using var _ = runtime;

        api.SwitchModel("new-model");

        await Assert.That(session.State.Model).IsEqualTo("new-model");
        await Assert.That(session.Model).IsEqualTo("new-model");
    }

    [Test]
    public async Task SwitchProvider_Delegates_ToSession()
    {
        var (session, runtime, api) = await BuildAsync();
        using var _ = runtime;

        var provider = new NullProvider();
        api.SwitchProvider(provider, "switched-provider", "switched-model");

        await Assert.That(session.State.ProviderName).IsEqualTo("switched-provider");
        await Assert.That(session.State.Model).IsEqualTo("switched-model");
    }
}
