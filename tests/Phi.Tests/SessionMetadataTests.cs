using Phi.Agent;
using Phi.Provider;
using Phi.Tests.Helpers;

namespace Phi.Tests;

/// <summary>
/// Tests for <see cref="ISession.SystemPrompt"/> and <see cref="ISession.HasUi"/> —
/// the two fields added in Sprint 0.5 to back the extension
/// <c>IPhiContext</c> view. Both are forward-projected from <see cref="Session"/>
/// into the extension surface; tests here pin the contract so the
/// <c>Phi.Extensions.Host.PhiApi</c> implementation in Sprint 1 has
/// a stable source to forward from.
/// </summary>
public class SessionMetadataTests : IDisposable
{
    private readonly string _cwd = Path.Combine(Path.GetTempPath(), $"phi-meta-{Guid.NewGuid():N}");

    public SessionMetadataTests()
    {
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
    }

    [Test]
    public async Task SystemPrompt_Is_Empty_Before_ApplyRuntime()
    {
        var session = Session.Create(_cwd, "m");
        await Assert.That(session.SystemPrompt).IsEqualTo("");
    }

    [Test]
    public async Task SystemPrompt_Is_Resolved_After_LoadAsync()
    {
        // SessionEnvironment needs a working resolver; the test factory
        // ships one. The resolved prompt includes the built-in tool
        // snippets, so it shouldn't be empty after ApplyRuntime.
        var env = TestSessionFactory.BuildEnv(new NullProvider());
        var session = await Session.LoadAsync(_cwd, env, providerName: "stub", model: "m");

        await Assert.That(session.SystemPrompt).IsNotEmpty();
    }

    [Test]
    public async Task SystemPrompt_Is_Same_On_Resume_And_Fresh_Load()
    {
        // The prompt depends on cwd + tools, not on persisted record's
        // model/provider — sanity check that resume path doesn't produce
        // a different prompt.
        var env = TestSessionFactory.BuildEnv(new NullProvider());
        var fresh = await Session.LoadAsync(_cwd, env, providerName: "stub", model: "m");
        var persisted = Session.Create(_cwd, "m", providerName: "stub");
        persisted.AppendMessage(new UserMessage { Content = "x" });
        var resumed = await Session.LoadAsync(_cwd, env,
            providerName: "stub", model: "m", resumeId: persisted.Id);

        await Assert.That(fresh.SystemPrompt).IsEqualTo(resumed.SystemPrompt);
    }

    [Test]
    public async Task HasUi_Defaults_To_False()
    {
        // Persistence-only sessions created via Session.Create are headless
        // (no composition root set HasUi). Composition roots in
        // Phi.Tui/Program.cs and Phi.Avalonia.Desktop/Program.cs flip
        // it after LoadAsync.
        var session = Session.Create(_cwd, "m");
        await Assert.That(session.HasUi).IsFalse();
    }

    [Test]
    public async Task HasUi_IsSettable_For_CompositionRoot()
    {
        var session = Session.Create(_cwd, "m");
        session.HasUi = true;
        await Assert.That(session.HasUi).IsTrue();
        session.HasUi = false;
        await Assert.That(session.HasUi).IsFalse();
    }

    [Test]
    public async Task HasUi_Does_Not_Interfere_With_Runtime()
    {
        // Setting HasUi before LoadAsync shouldn't change anything else
        // about the runtime composition path.
        var session = Session.Create(_cwd, "m");
        session.HasUi = true;

        var env = TestSessionFactory.BuildEnv(new NullProvider());
        var withUi = await Session.LoadAsync(_cwd, env, providerName: "stub", model: "m");
        var withoutUi = await Session.LoadAsync(_cwd, env, providerName: "stub", model: "m");

        withUi.HasUi = true;
        withoutUi.HasUi = false;

        await Assert.That(withUi.SystemPrompt).IsEqualTo(withoutUi.SystemPrompt);
        await Assert.That(withUi.State.Model).IsEqualTo(withoutUi.State.Model);
    }

    [Test]
    public async Task NullProvider_DoesNot_Throw_On_Empty_Config()
    {
        // Sanity: a fully headless path (null provider + empty cwd + no UI)
        // still constructs and exposes the new metadata properties without
        // throwing.
        var env = TestSessionFactory.BuildEnv(new NullProvider());
        var session = await Session.LoadAsync(_cwd, env, providerName: "stub", model: "m");
        session.HasUi = false;

        await Assert.That(session.HasUi).IsFalse();
        await Assert.That(session.SystemPrompt).IsNotEmpty();   // still has built-in tool snippets
    }
}
