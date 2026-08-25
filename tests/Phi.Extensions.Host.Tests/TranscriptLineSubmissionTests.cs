using Phi.Agent;
using Phi.Chat;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Sprint 4: extension-submitted transcript lines (<c>IPhiApi.SubmitTranscriptLine</c>)
/// and renderer registration (<c>IPhiApi.RegisterTranscriptLineRenderer</c>).
/// Verifies the full path: Setup registers a renderer → the renderer is
/// queryable via <see cref="IExtensionRenderers"/> → a submitted
/// <see cref="Phi.Extensions.TranscriptLine"/> routes through the UI
/// bridge into a <see cref="ChatTranscriptProjector"/> as a
/// <see cref="CustomLine"/>.
/// </summary>
[NotInParallel("transcript-line")]
public class TranscriptLineSubmissionTests : IDisposable
{
    private readonly string _cwd;

    public TranscriptLineSubmissionTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-tl-{Guid.NewGuid():N}");
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

    /// <summary>
    /// Extension that captures its <see cref="IPhiApi"/> during Setup and
    /// registers a transcript-line renderer. The captured api lets the test
    /// drive <c>SubmitTranscriptLine</c> after <c>Initialize</c>.
    /// </summary>
    [PhiExtension(
        Name = "tl-test-ext",
        Version = "1.0.0",
        Description = "Test extension for transcript line submission.",
        Capabilities = ExtensionCapability.TranscriptWrite | ExtensionCapability.UiInteract)]
    private sealed class LineExtension(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api)
        {
            api.RegisterTranscriptLineRenderer("my-ext:hello", (line, expanded) =>
            {
                var who = line.Details?.TryGetValue("who", out var w) == true
                    ? w?.ToString() ?? "world" : "world";
                return $"👋 hello {who}";
            });
            onSetup(api);
        }
    }

    /// <summary>A bridge that records submitted transcript lines instead of discarding them.</summary>
    private sealed class RecordingBridge : IPhiUiBridge
    {
        public bool HasUi => false;
        public List<TranscriptLine> Lines { get; } = [];

        public void Notify(string message, NotifyLevel level) { }
        public void NotifyStatus(string message) { }
        public void FlashError(string message, bool persistent) { }
        public void SubmitTranscriptLine(TranscriptLine line) => Lines.Add(line);
        public Task<string?> SelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
            => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, TimeSpan? timeout)
            => Task.FromResult(false);
        public Task<string?> InputAsync(string title, string placeholder, TimeSpan? timeout)
            => Task.FromResult<string?>(null);
    }

    private async Task<(Phi.Session Session, ExtensionRuntime Runtime, IPhiApi Api, RecordingBridge Bridge)>
        BuildAsync()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var bridge = new RecordingBridge();
        var runtime = new ExtensionRuntime(session, bridge);

        IPhiApi? captured = null;
        runtime.RegisterCompiledExtension(new LineExtension(api => captured = api));
        runtime.Initialize();
        return (session, runtime, captured!, bridge);
    }

    [Test]
    public async Task RegisterTranscriptLineRenderer_IsQueryable_ThroughIExtensionRenderers()
    {
        var (_, runtime, _, _) = await BuildAsync();
        using var rt = runtime;

        var found = rt.TryGetTranscriptLineRenderer("my-ext:hello", out var renderer);

        await Assert.That(found).IsTrue();
        await Assert.That(renderer).IsNotNull();

        // No renderer registered for an unknown type.
        await Assert.That(rt.TryGetTranscriptLineRenderer("nope:missing", out _)).IsFalse();
    }

    [Test]
    public async Task SubmitTranscriptLine_RoutesThroughBridge()
    {
        var (_, runtime, api, bridge) = await BuildAsync();
        using var rt = runtime;

        var line = new TranscriptLine(
            "my-ext:hello",
            "line-1",
            "Building…",
            new Dictionary<string, object?> { ["who"] = "Phi" });
        api.SubmitTranscriptLine(line);

        await Assert.That(bridge.Lines).Count().IsEqualTo(1);
        await Assert.That(bridge.Lines[0].Type).IsEqualTo("my-ext:hello");
        await Assert.That(bridge.Lines[0].Content).IsEqualTo("Building…");
    }

    [Test]
    public async Task Projector_SubmitCustomLine_AddsCustomLine_WithRendererAccess()
    {
        var (session, runtime, _, _) = await BuildAsync();
        using var rt = runtime;

        var projector = new ChatTranscriptProjector(session, rt);
        projector.SubmitCustomLine("my-ext:hello", "line-1", "Building…", new Dictionary<string, object?> { ["who"] = "Phi" });

        await Assert.That(projector.Current).Count().IsEqualTo(1);
        await Assert.That(projector.Current[0]).IsTypeOf<CustomLine>();
        var line = (CustomLine)projector.Current[0];
        await Assert.That(line.LineType).IsEqualTo("my-ext:hello");
        await Assert.That(line.Content).IsEqualTo("Building…");
        await Assert.That(line.Details!["who"]).IsEqualTo("Phi");

        // The renderer registered on the runtime is reachable through the
        // projector's Renderers view (the same instance the UI reads).
        var found = projector.Renderers!.TryGetTranscriptLineRenderer("my-ext:hello", out var renderer);
        await Assert.That(found).IsTrue();
        var dto = new TranscriptLine(line.LineType, line.Id, line.Content, line.Details);
        var fragment = ((Phi.Extensions.Rendering.TranscriptLineRenderer)renderer!)(dto, Expanded: false);
        await Assert.That(fragment).IsEqualTo("👋 hello Phi");
    }

    [Test]
    public async Task SubmitCustomLine_EmptyId_AssignsGeneratedId()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        using var projector = new ChatTranscriptProjector(session, renderers: null);

        projector.SubmitCustomLine("ext:thing", id: null, content: "body");

        await Assert.That(projector.Current[0]).IsTypeOf<CustomLine>();
        var line = (CustomLine)projector.Current[0];
        await Assert.That(line.Id).IsNotEmpty();
    }

    [Test]
    public async Task SubmitCustomLine_WithoutRenderers_FallsBackToContent_IsRendered()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        using var projector = new ChatTranscriptProjector(session, renderers: null);

        projector.SubmitCustomLine("ext:thing", "l1", "plain body");

        await Assert.That(projector.Current[0]).IsTypeOf<CustomLine>();
        var line = (CustomLine)projector.Current[0];
        await Assert.That(line.LineType).IsEqualTo("ext:thing");
        await Assert.That(line.Content).IsEqualTo("plain body");
        await Assert.That(line.Details).IsNull();
    }

    /// <summary>Companion to the projector test: the runtime implements IExtensionRenderers.</summary>
    [Test]
    public async Task Runtime_Implements_IExtensionRenderers()
    {
        var (_, runtime, _, _) = await BuildAsync();
        using var rt = runtime;

        await Assert.That(rt).IsAssignableTo<IExtensionRenderers>();
    }
}
