using Phi.Extensions.Host.Tests.Helpers;
using Phi.Agent;
using Phi.Prompts;
using Phi.Provider;
using Phi.Providers;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Locks down <see cref="BuiltInExtensions"/>: the single, version-
/// controlled place where the host enumerates the extensions it always
/// loads (today: <c>CodingPack</c>; future built-ins append here, not at
/// every composition root).
/// <para>
/// The assertions cover the contract that any new built-in must also
/// satisfy: (a) it shows up in <see cref="ExtensionRuntime.Extensions"/>
/// after <see cref="BuiltInExtensions.RegisterAll"/>, (b) its tools
/// become callable after <see cref="ExtensionRuntime.Initialize"/>,
/// (c) its slash commands register in
/// <see cref="ExtensionRuntime.Commands"/>, and (d) calling
/// <c>RegisterAll</c> twice is a no-op the second time — neither the
/// Extensions count nor the cumulative system prompt doubles up.
/// </para>
/// </summary>
[NotInParallel("built-in-extensions")]
public class BuiltInExtensionsTests : IDisposable
{
    private readonly string _cwd;
    private readonly TestPhiHome.Scope _phiHome = new();

    public BuiltInExtensionsTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-builtin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
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
        SystemPrompt = new SystemPromptOptions { ResolvedSystemPrompt = "built-in test" },
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
        var session = await Phi.Session.LoadAsync(_cwd, BuildEnv(), providerName: "stub", model: "m");
        session.HasUi = false;
        var runtime = new ExtensionRuntime(session, new NullPhiUiBridge());
        BuiltInExtensions.RegisterAll(runtime);
        runtime.Initialize();
        return (session, runtime);
    }

    [Test]
    public async Task RegisterAll_AddsCodingPack_ToExtensionList()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        var names = runtime.Extensions.Select(e => e.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["coding-pack"]);
        await Assert.That(runtime.SetupResults).IsEmpty();
    }

    [Test]
    public async Task RegisterAll_RegistersFourCodingTools()
    {
        var (session, runtime) = await BuildAsync();
        using var _ = runtime;

        // CodingPack owns bash / read / write / edit. Each tool registers
        // itself via IPhiApi.RegisterTool, which appends a "tool `NAME`:
        // SNIPPET" guideline to the session's system prompt. So the proof
        // that all four tools ran their Setup is that all four snippets
        // appear in session.SystemPrompt after Initialize.
        //
        // We deliberately don't assert TryGetToolDescriptor — that lookup
        // is fed by RegisterToolCard, which CodingPack doesn't use. The
        // static built-in table (ToolDescriptors.For) covers the four
        // names regardless of whether an extension overrode it, so it
        // can't tell us whether CodingPack's Setup ran.
        var prompt = session.SystemPrompt;
        var expectedSnippets = new[]
        {
            "tool `bash`: bash: Run a shell command",
            "tool `read`: read: Read a file from the local workspace",
            "tool `write`: write: Create a new file",
            "tool `edit`: edit: Make a surgical edit",
        };
        foreach (var snippet in expectedSnippets)
        {
            await Assert.That(prompt).Contains(snippet);
        }
    }

    [Test]
    public async Task RegisterAll_RegistersToolsSlashCommand()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        // /tools is CodingPack's live demo of the slash-command pipeline.
        // It's also the only way a user can ask "what built-in tools are
        // available?" without an LLM round-trip, so it must survive the
        // built-in move. RegisterCommand normalizes the key (trims a
        // leading '/'), so the dictionary key is "tools".
        await Assert.That(runtime.Commands.ContainsKey("tools")).IsTrue();
        var entry = runtime.Commands["tools"];
        await Assert.That(entry.Description).IsEqualTo("List the coding-pack tools.");
    }

    [Test]
    public async Task RegisterAll_IsIdempotent_DoesNotDoubleExtensions()
    {
        var (_, runtime) = await BuildAsync();
        using var _ = runtime;

        // Snapshot the post-first-call state, then call RegisterAll
        // again WITHOUT re-running Initialize. The contract is: the second
        // call is a no-op on the extension list. (If the caller also
        // re-ran Initialize, CodingPack's Setup would fire a second time
        // and re-append its prompt guidelines — that's a separate
        // Initialize-isn't-reentrant question, not BuiltInExtensions'
        // responsibility. The reload path builds a fresh runtime, so
        // double-Initialize on the same runtime doesn't happen in
        // production.)
        var extensionCountBefore = runtime.Extensions.Count;
        var namesBefore = runtime.Extensions.Select(e => e.Name).ToList();

        BuiltInExtensions.RegisterAll(runtime);

        await Assert.That(runtime.Extensions.Count).IsEqualTo(extensionCountBefore);
        await Assert.That(runtime.Extensions.Select(e => e.Name)).IsEquivalentTo(namesBefore);
    }
}
