using Phi.Agent;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Prompts;
using Phi.Provider;
using Phi.Providers;
using Phi.Tests.Helpers;
using Phi.Tui.Components.ToolCards;

namespace Phi.Tests;

/// <summary>
/// UI-facing tests for the Sprint 4 demo extension. Verifies that the TUI
/// and Avalonia registries both resolve the custom tool card for
/// <c>demo</c> and that a string-returning renderer is wrapped correctly by
/// each host.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class CustomCardDemoUiTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _demoPath;

    public CustomCardDemoUiTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-demo-ui-{Guid.NewGuid():N}");
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

    private async Task<(Phi.Session Session, ExtensionRuntime Runtime)> BuildAsync()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "demo-model");
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_demoPath]);
        runtime.Initialize();
        return (session, runtime);
    }

    [Test]
    public async Task TuiRegistry_Uses_CustomCard_ForDemo_AndWrapsStringBody()
    {
        var (_, runtime) = await BuildAsync();
        using var rt = runtime;

        var card = ToolCardRegistry.For("demo", rt);
        await Assert.That(card).IsTypeOf<CustomToolCard>();

        var call = new ToolCall("d1", "demo")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["text"] = "hello" },
        };
        card.ShowPending(call);
        card.Complete(new ToolResult([new TextBlock("ok")]));

        var custom = (CustomToolCard)card;
        // String-returning renderer is wrapped as a Markup body.
        await Assert.That(custom.BodyState.Value).IsTypeOf<XenoAtom.Terminal.UI.Controls.Markup>();
    }

    [Test]
    public async Task Projector_Uses_CustomDescriptor_ForDemoToolCall()
    {
        var session = new MockSession();
        var (_, runtime) = await BuildAsync();
        using var rt = runtime;
        using var projector = new Phi.Chat.ChatTranscriptProjector(session, rt);

        var call = new ToolCall("d1", "demo") { Arguments = [] };
        session.EmitHarnessEvent(new MessageUpdateEvent(
            new AssistantMessage(), new ToolCallEvent(call)));

        var line = projector.Current.OfType<Phi.Chat.ToolCallLine>().Single();
        await Assert.That(line.Descriptor.IconKey).IsEqualTo("🎨");
    }
}
