using Phi.Agent;
using Phi.Prompts;
using Phi.Provider;
using Phi.Providers;
using Phi.Slash;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Sprint 4.5: slash command dispatcher. Verifies that
/// <see cref="IPhiApi.RegisterCommand"/> registrations reach the host via
/// <see cref="ISlashCommandRegistry"/> and that <c>TryDispatch</c> hits the
/// right handler with the right args. Also covers the key-normalisation
/// rule (<c>"/foo"</c> and <c>"foo"</c> land in the same bucket) and the
/// fail-safe handler-exception path.
/// </summary>
[NotInParallel("slash-dispatch")]
public class SlashCommandDispatcherTests : IDisposable
{
    private readonly string _cwd;

    public SlashCommandDispatcherTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-slash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
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

    /// <summary>Helper extension that registers one or more slash commands and
    /// captures the api so the test can drive <c>TryDispatch</c>.</summary>
    [PhiExtension(
        Name = "slash-dispatch-test",
        Version = "1.0.0",
        Description = "Registers slash commands for dispatcher tests.",
        Capabilities = ExtensionCapability.UiInteract)]
    private sealed class CapturingExtension(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api)
        {
            api.RegisterCommand(
                "/hello",
                (args, _) => $"hello{(string.IsNullOrEmpty(args) ? "" : $" {args}")}",
                description: "Greet someone via slash command.");
            api.RegisterCommand(
                "boom",  // no leading '/' — dispatcher normalises both forms
                (_, _) => throw new InvalidOperationException("simulated handler crash"));
            onSetup(api);
        }
    }

    private async Task<(Phi.Session Session, ExtensionRuntime Runtime)> BuildAsync()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        runtime.RegisterCompiledExtension(new CapturingExtension(_ => { }));
        runtime.Initialize();
        return (session, runtime);
    }

    [Test]
    public async Task TryDispatch_HitHandler_ReturnsHandlerResult()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        var hit = runtime.TryDispatch("hello", "world", runtime.Context, out var result);
        await Assert.That(hit).IsTrue();
        await Assert.That(result).IsEqualTo("hello world");
    }

    [Test]
    public async Task TryDispatch_HitHandler_NoArgs_ReturnsBareResult()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        var hit = runtime.TryDispatch("hello", "", runtime.Context, out var result);
        await Assert.That(hit).IsTrue();
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task TryDispatch_LeadingSlashIsStripped_BeforeLookup()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        // Both "/hello" and "hello" should resolve to the same handler; the
        // dispatcher treats the leading slash as a UI affordance, not part
        // of the key.
        runtime.TryDispatch("/hello", "x", runtime.Context, out var r1);
        await Assert.That(r1).IsEqualTo("hello x");
        runtime.TryDispatch("hello", "x", runtime.Context, out var r2);
        await Assert.That(r2).IsEqualTo("hello x");
        await Assert.That(r1).IsEqualTo(r2);
    }

    [Test]
    public async Task TryDispatch_UnknownCommand_ReturnsFalse_NoThrow()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        var hit = runtime.TryDispatch("nope", "", runtime.Context, out var result);
        await Assert.That(hit).IsFalse();
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryDispatch_HandlerThrows_SwallowsAndReturnsFailureMessage()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        // The "boom" handler throws. The dispatcher must surface the
        // exception as a transient-style message (still "handled" so the
        // caller doesn't fall through to submit-as-prompt) instead of
        // crashing the prompt dispatch path.
        var hit = runtime.TryDispatch("boom", "args", runtime.Context, out var result);
        await Assert.That(hit).IsTrue();
        await Assert.That(result).DoesNotContain("boom args");
        await Assert.That(result).Contains("failed");
        await Assert.That(result).Contains("simulated handler crash");
    }

    [Test]
    public async Task AllCommands_ListsRegisteredCommands()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        // Two registrations (one with leading '/', one without).
        await Assert.That(runtime.AllCommands.Count()).IsEqualTo(2);
        await Assert.That(runtime.AllCommands.Select(c => c.Name))
            .IsEquivalentTo(new[] { "/hello", "/boom" });
    }
}
