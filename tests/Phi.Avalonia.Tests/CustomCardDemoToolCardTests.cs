using Phi.Agent;
using Phi.Avalonia.Components.ToolCards;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Prompts;
using Phi.Provider;
using Phi.Providers;

namespace Phi.Avalonia.Tests;

/// <summary>
/// Avalonia-side registry test for the Sprint 4 demo extension. Verifies
/// the Avalonia registry resolves the custom card for <c>demo</c> and that
/// a string-returning renderer is wrapped without throwing.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class CustomCardDemoToolCardTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;
    private readonly string _previousPhiHome;
    private readonly string _demoPath;

    public CustomCardDemoToolCardTests()
    {
        AvaloniaTestHost.EnsureInitialized();
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-demo-avalonia-{Guid.NewGuid():N}");
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-demo-avalonia-home-{Guid.NewGuid():N}");
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _phiHome;
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
        SessionPaths.PhiHome = _previousPhiHome;
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
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

    private async Task<ExtensionRuntime> BuildRuntimeAsync()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "demo-model");
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.DiscoverAndLoad([_demoPath]);
        runtime.Initialize();
        return runtime;
    }

    [Test]
    public async Task AvaloniaRegistry_Uses_CustomCard_ForDemo()
    {
        using var runtime = await BuildRuntimeAsync();

        var card = AvaloniaToolCardRegistry.For("demo", runtime);
        await Assert.That(card).IsTypeOf<CustomToolCardView>();

        var call = new ToolCall("d1", "demo")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["text"] = "hello" },
        };
        card.ShowPending(call);
        card.Complete(new ToolResult([new TextBlock("ok")]));

        await Assert.That(card.Visual).IsNotNull();
    }
}
