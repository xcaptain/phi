using Phi.Agent;
using Phi.Extensions.CodingPack;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Prompts;
using Phi.Providers;
using Phi.Provider;

namespace Phi.Tests;

/// <summary>
/// Sprint 2.5 end-to-end: the CodingPack extension (compile-time, referenced
/// via ProjectReference) registers the four default coding tools into the
/// session's harness — proving the "default coding capability ships as an
/// extension" architecture works without breaking the agent loop.
/// </summary>
[NotInParallel("coding-pack")]
public class CodingPackIntegrationTests : IDisposable
{
    private readonly string _cwd;

    public CodingPackIntegrationTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-codingpack-{Guid.NewGuid():N}");
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
        ExtensionRuntimeFactory = session =>
        {
            var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
            runtime.RegisterCompiledExtension(new CodingPackExt());
            runtime.Initialize();
            return runtime;
        },
    };

    private sealed class FixedResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }

    [Test]
    public async Task CodingPack_RegistersFourTools_IntoHarness()
    {
        // Compose a session exactly like Phi.Tui's Program.cs does: the
        // env's ExtensionRuntimeFactory registers CodingPack automatically.
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;

        var names = session.HarnessForTest().Tools.Select(t => t.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["bash", "read", "write", "edit"]);
    }

    [Test]
    public async Task CodingPack_Survives_NewSessionAsync()
    {
        // Regression test: env.ExtensionRuntimeFactory (not a one-off wire-up
        // in Program.cs) is what makes CodingPack's tools reappear after a
        // session switch. Before this, /new and /sessions silently dropped
        // every tool because nothing re-ran RegisterCompiledExtension against
        // the freshly loaded session.
        var env = BuildEnv();
        var first = await Phi.Session.LoadAsync(_cwd, env, providerName: "stub", model: "m");
        first.HasUi = false;
        await Assert.That(first.HarnessForTest().Tools.Select(t => t.Name))
            .IsEquivalentTo(["bash", "read", "write", "edit"]);

        var next = (Phi.Session)await first.NewSessionAsync();
        try
        {
            await Assert.That(next.HarnessForTest().Tools.Select(t => t.Name))
                .IsEquivalentTo(["bash", "read", "write", "edit"]);
        }
        finally
        {
            next.Dispose();
        }
    }

    [Test]
    public async Task CodingPack_Survives_ReloadExtensions()
    {
        // Sprint 2 wiring: /reload must keep CodingPack's four tools.
        // Before this, ExtensionReloader only rebuilt dll-discovered
        // extensions and never re-ran RegisterCompiledExtension, so reload
        // silently dropped bash/read/write/edit. SessionEnvironment
        // .ExtensionRuntimeFactory (now called on every LoadAsync and on
        // ReloadExtensions) re-registers compiled extensions automatically.
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        await Assert.That(session.HarnessForTest().Tools.Select(t => t.Name))
            .IsEquivalentTo(["bash", "read", "write", "edit"]);

        session.ReloadExtensions();

        await Assert.That(session.HarnessForTest().Tools.Select(t => t.Name))
            .IsEquivalentTo(["bash", "read", "write", "edit"]);
    }

    [Test]
    public async Task ReloadExtensions_WithoutEnv_Throws_LeavesSessionUsable()
    {
        // Persistence-only sessions (built by Session.GetOrCreateDefault or
        // the test static factories) have no env, so no factory, no
        // runtime to reload. ReloadExtensions must report that clearly —
        // callers (the TUI /reload slash command) render the exception as
        // a transient and the user can keep using the session.
        var session = Phi.Session.GetOrCreateDefault(_cwd, "m");
        var ex = Assert.Throws<InvalidOperationException>(() => session.ReloadExtensions());
        await Assert.That(ex!.Message).Contains("SessionEnvironment");
    }

    [Test]
    public async Task CodingPack_Tools_Are_Invocable()
    {
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;

        // Write a file with the write tool, then read it back.
        var writeTool = session.HarnessForTest().Tools.Single(t => t.Name == "write");
        var writeArgs = System.Text.Json.Nodes.JsonNode.Parse(
            """{"path":"hello.txt","content":"hi from coding pack"}""")!.AsObject();
        var writeResult = await writeTool.ExecuteAsync("write", "c1", writeArgs, default);
        await Assert.That(writeResult.IsError).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(_cwd, "hello.txt"))).IsEqualTo("hi from coding pack");

        var readTool = session.HarnessForTest().Tools.Single(t => t.Name == "read");
        var readArgs = System.Text.Json.Nodes.JsonNode.Parse(
            """{"path":"hello.txt"}""")!.AsObject();
        var readResult = await readTool.ExecuteAsync("read", "c2", readArgs, default);
        await Assert.That(readResult.IsError).IsFalse();
        await Assert.That(readResult.Text).Contains("hi from coding pack");
    }
}

/// <summary>Test-only harness accessor (same pattern as Phi.Extensions.Host.Tests).</summary>
internal static class CodingPackTestSessionAccessor
{
    public static Phi.Agent.Harness HarnessForTest(this Phi.Session session) =>
        session.GetType()
            .GetField("_harness", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session) as Phi.Agent.Harness
        ?? throw new InvalidOperationException("harness not initialized");
}
