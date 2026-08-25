using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Sprint 4: extension tool-card registration
/// (<c>IPhiApi.RegisterToolCard</c>) and dynamic descriptor / card lookup
/// through <see cref="IExtensionRenderers"/>.
/// </summary>
[NotInParallel("tool-card-reg")]
public class ToolCardRegistrationTests : IDisposable
{
    private readonly string _cwd;

    public ToolCardRegistrationTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-tc-{Guid.NewGuid():N}");
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

    [PhiExtension(
        Name = "tc-test-ext",
        Version = "1.0.0",
        Description = "Test extension registering a custom tool card.",
        Capabilities = ExtensionCapability.None)]
    private sealed class CardExtension(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api)
        {
            api.RegisterToolCard(
                "deploy",
                new ToolDescriptor(ToolKind.Generic, "deploy", "🚀"),
                renderer: (args, result) => $"deploy to {args["env"]} → {result.Text}");
            onSetup(api);
        }
    }

    private async Task<(Phi.Session Session, ExtensionRuntime Runtime)> BuildAsync()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.RegisterCompiledExtension(new CardExtension(_ => { }));
        runtime.Initialize();
        return (session, runtime);
    }

    [Test]
    public async Task RegisterToolCard_StoresDescriptor_AndRenderer()
    {
        var (_, runtime) = await BuildAsync();
        using var rt = runtime;

        // Descriptor is overrideable.
        var hasDescriptor = rt.TryGetToolDescriptor("deploy", out var descriptor);
        await Assert.That(hasDescriptor).IsTrue();
        await Assert.That(descriptor!.Title).IsEqualTo("deploy");
        await Assert.That(descriptor!.IconKey).IsEqualTo("🚀");

        // Renderer is queryable and callable.
        var hasRenderer = rt.TryGetToolCardRenderer("deploy", out var renderer);
        await Assert.That(hasRenderer).IsTrue();
        var args = System.Text.Json.Nodes.JsonNode.Parse("""{"env":"prod"}""")!;
        var fragment = ((Phi.Extensions.Rendering.ToolCardRenderer)renderer!)(args,
            new ToolResult([new TextBlock("ok")]));
        await Assert.That(fragment).IsEqualTo("deploy to prod → ok");
    }

    [Test]
    public async Task UnknownTool_FallsBack_ToBuiltInDescriptor_AndNoRenderer()
    {
        var (_, runtime) = await BuildAsync();
        using var rt = runtime;

        // "bash" has no extension override → TryGetToolDescriptor returns
        // false (caller uses the built-in table) and no renderer.
        await Assert.That(rt.TryGetToolDescriptor("bash", out _)).IsFalse();
        await Assert.That(rt.TryGetToolCardRenderer("bash", out _)).IsFalse();
    }
}
