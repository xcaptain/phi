using Phi;
using Phi.Extensions.CodingPack;
using Phi.Extensions.Host;
using Phi.Providers;
using Phi.Tui;

// ──────── CLI args ────────
// phi                  → /sessions/new (fresh session, persisted lazily)
// phi --session <id>   → /sessions/:id (resume an indexed session)
string? resumeSessionId = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--session" && i + 1 < args.Length)
    {
        resumeSessionId = args[++i];
    }
    else
    {
        Console.Error.WriteLine($"Unknown argument: {args[i]}");
        Console.Error.WriteLine("Usage: phi [--session <id]>");
        return 1;
    }
}

// Composition root: wire the provider manager into a SessionEnvironment
// (the cross-session context shared by every Session the TUI will ever
// create) and use it to build the initial Session. Subsequent /new and
// /sessions are handled by ISession.NewSessionAsync / ResumeAsync — no
// separate navigator. Provider construction lives inside Session.LoadAsync,
// which resolves via SessionEnvironment.ProviderResolver and falls back to
// a no-op provider when no API key exists (so the TUI can open and prompt
// for /connect).
var providerManager = new ProviderManager();
var defaultProvider = providerManager.ResolveDefaultProvider();

// Sprint 3: real PhiUiBridge for extensions. The sink wraps the chat
// page's transcript + status bar + dialog shower; PhiTuiApp rebuilds
// the page on every session switch and updates `currentSink` via the
// SinkBuilt callback below, so the bridge always forwards to the live
// UI. Before PhiTuiApp builds its first page, extensions hitting the
// bridge get the no-op NullUiSink (HasUi=false → dialogs return
// defaults, no-ops are silent).
IUiSink currentSink = new NullUiSink();
// Stashed so the UI can resolve the session's extension renderers
// (tool cards / transcript lines) after LoadAsync has built the runtime.
// Each new session rebuilds the runtime and re-stashes it; the UI's
// renderers accessor reads whatever is current.
Phi.Extensions.Host.ExtensionRuntime? currentRuntime = null;
var env = SessionEnvironment.Default(providerManager,
    // Sync factory — used by /reload (which is intentionally sync).
    extensionRuntimeFactory: session =>
    {
        var bridge = new PhiUiBridge(() => currentSink);
        var runtime = new ExtensionRuntime(session, bridge);
        currentRuntime = runtime;
        runtime.RegisterCompiledExtension(new CodingPackExt());
        runtime.Initialize();
        return runtime;
    },
    // Async factory — used by Session.LoadAsync. Runs the Project
    // Trust gate (asks the user via IUiSink.ConfirmAsync) before
    // loading any project-level extensions under {cwd}/.phi/extensions/.
    // First session start blocks on the dialog; subsequent starts
    // hit the cached decision in ~/.phi/trust.json.
    extensionRuntimeFactoryAsync: async session =>
    {
        var bridge = new PhiUiBridge(() => currentSink);
        var runtime = new ExtensionRuntime(session, bridge);
        currentRuntime = runtime;
        runtime.RegisterCompiledExtension(new CodingPackExt());
        await runtime.DiscoverAndTrustProjectExtensionsAsync(session.Cwd);
        runtime.Initialize();
        return runtime;
    });

Session session;
try
{
    session = await Session.LoadAsync(
        Environment.CurrentDirectory, env,
        providerName: defaultProvider.Name,
        model: providerManager.ResolveDefaultModel(defaultProvider),
        resumeId: resumeSessionId);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

using (session)
{
    session.HasUi = true;   // TUI hosts a real UI; surfaced via IPhiContext.Ui.HasUi to extensions

    var app = new PhiTuiApp(session, providerManager,
        // The dialog shower is constructed with the no-op marshaller;
        // PhiTuiApp.Run() rebuilds it around the TerminalApp's
        // dispatcher once that exists. This is the only way to satisfy
        // the ctor's non-null TuiUiThread without racing the Run() loop.
        new TuiDialogShower(TuiUiThread.None),
        onSinkBuilt: sink => currentSink = sink,
        renderersAccessor: () => currentRuntime,
        commandsAccessor: () => currentRuntime,
        contextAccessor: () => currentRuntime?.Context);
    app.Run();
}
return 0;
