using Phi.Agent;
using Phi.Extensions.Host.Tests.Helpers;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// End-to-end for the Sprint 4 demo extension: load the built
/// CustomCardDemo dll, run Setup, verify the demo tool is invocable and
/// the custom card / transcript-line registrations are visible through
/// <see cref="IExtensionRenderers"/>.
/// </summary>
[NotInParallel("custom-card-demo")]
public class CustomCardDemoIntegrationTests : IDisposable
{
    private readonly string _cwd;
    private readonly TestPhiHome.Scope _phiHome = new();
    private readonly string _demoPath;

    public CustomCardDemoIntegrationTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-demo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Phi.Extensions.CustomCardDemo.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "extensions", "CustomCardDemo", "bin", "Debug", "net10.0", "Phi.Extensions.CustomCardDemo.dll"),
        };
        _demoPath = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("CustomCardDemo.dll not found", candidates[0]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _phiHome.Dispose();
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
    public async Task Load_Instantiates_CustomCardDemo_And_Discovers_Attribute()
    {
        var loaded = ExtensionLoader.Load(_demoPath);

        await Assert.That(loaded.Name).IsEqualTo("custom-card-demo");
        await Assert.That(loaded.Version).IsEqualTo("1.0.0");
        await Assert.That(loaded.Description).Contains("custom tool cards");
        await Assert.That(loaded.Instance).IsAssignableTo<IPhiExtension>();
    }

    [Test]
    public async Task Runtime_Initialize_Registers_DemoTool_Card_And_TranscriptRenderer()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "demo-model");
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_demoPath]);
        runtime.Initialize();

        // Tool is in the live harness.
        var names = session.HarnessForTest().Tools.Select(t => t.Name).ToList();
        await Assert.That(names).Contains("demo");

        // Descriptor + renderer are queryable through IExtensionRenderers.
        var hasDescriptor = runtime.TryGetToolDescriptor("demo", out var descriptor);
        await Assert.That(hasDescriptor).IsTrue();
        await Assert.That(descriptor!.IconKey).IsEqualTo("🎨");

        var hasCardRenderer = runtime.TryGetToolCardRenderer("demo", out var cardRenderer);
        await Assert.That(hasCardRenderer).IsTrue();

        var hasLineRenderer = runtime.TryGetTranscriptLineRenderer("custom-card-demo:notice", out var lineRenderer);
        await Assert.That(hasLineRenderer).IsTrue();

        // Both renderers return cross-host strings.
        var cardText = ((Phi.Extensions.Rendering.ToolCardRenderer)cardRenderer!)(
            System.Text.Json.Nodes.JsonNode.Parse("""{"text":"hello"}""")!,
            new ToolResult([new TextBlock("ok")]))!;
        await Assert.That(cardText).IsEqualTo("demo card (hello) → ok");

        var lineText = ((Phi.Extensions.Rendering.TranscriptLineRenderer)lineRenderer!)(
            new Phi.Extensions.TranscriptLine(
                "custom-card-demo:notice",
                "line-1",
                "payload",
                new Dictionary<string, object?> { ["level"] = "warn" }),
            Expanded: false);
        await Assert.That(lineText).IsEqualTo("[warn] payload");
    }

    [Test]
    public async Task DemoTool_ExecuteTypedAsync_DefaultsText()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "demo-model");
        using var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_demoPath]);
        runtime.Initialize();

        var tool = session.HarnessForTest().Tools.Single(t => t.Name == "demo");
        var result = await tool.ExecuteAsync(
            "demo",
            "call-1",
            System.Text.Json.Nodes.JsonNode.Parse("""{}""")!.AsObject(),
            default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(((TextBlock)result.Content[0]).Text).IsEqualTo("hello from demo");
    }
}
