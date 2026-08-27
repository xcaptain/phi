using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Phi.Avalonia.Controls;
using Phi.Agent;
using Phi.Chat;
using Phi.Extensions;
using Phi.Providers;
using Phi.Slash;

namespace Phi.Avalonia.Components;

/// <summary>
/// Avalonia shell's <see cref="ISlashActionSink"/>. The
/// <see cref="SlashInputDispatcher"/> is UI-agnostic; this class maps
/// every command onto the desk's existing primitives — <see cref="ActiveSession"/>
/// for navigation, <see cref="Phi.Avalonia.DeskLog"/> / persistent
/// transcript lines for transient feedback, the running
/// <see cref="ISession"/> for submit / steer / load-skill / reload, and
/// <see cref="ApplicationLifetime"/> for <c>/exit</c>.
/// <para>
/// Dialog-based commands (<c>/sessions</c>, <c>/connect</c> bare,
/// <c>/models</c>) report a non-null guidance message instead of opening a
/// dialog: the same actions are reachable via the sidebar (sessions) and
/// the model picker footer combo (connect / models), which is the
/// consistent point-and-click UX the desk promotes over keyboard-driven
/// pickers. <c>/connect &lt;provider&gt;</c> still works and switches
/// providers through the existing <see cref="ProviderManager"/> path.
/// </para>
/// </summary>
internal sealed class AvaloniaSlashActionSink : ISlashActionSink
{
    private readonly ISession _session;
    private readonly ActiveSession _active;
    private readonly ProviderManager _providers;
    private readonly Action<Action> _postToUi;
    private readonly ChatTranscriptProjector? _projector;

    public AvaloniaSlashActionSink(
        ISession session,
        ActiveSession active,
        ProviderManager providers,
        Action<Action> postToUi,
        ChatTranscriptProjector? projector = null)
    {
        _session = session;
        _active = active;
        _providers = providers;
        _postToUi = postToUi;
        _projector = projector;
    }

    public void SubmitPrompt(string text)
    {
        _projector?.SubmitUserLine(text);
        _session.SubmitPrompt(text);
    }

    public void EnqueueSteering(string text)
    {
        // Surface the queued steering message so it shows up in the
        // transcript while a turn is in flight (mirrors TUI's
        // ShowTransient + EnqueueSteering pair).
        _projector?.SubmitUserLine(text);
        _session.EnqueueSteering(new UserMessage { Content = text });
    }

    public void NavigateToNew()
    {
        _postToUi(() => _ = NavigateToNewAsync());
    }

    public void ReloadExtensions()
    {
        try
        {
            _session.ReloadExtensions();
            DeskLog.Write("AvaloniaSlashActionSink.ReloadExtensions: ok");
            // Acknowledge via the running projector so the user sees
            // feedback inline (mirrors ShellView.ReloadExtensions in the
            // sidebar row's ⋯ menu).
            _projector?.SubmitPersistentError("Extensions reloaded.");
        }
        catch (Exception ex)
        {
            DeskLog.Write($"AvaloniaSlashActionSink.ReloadExtensions: threw: {ex}");
            _projector?.SubmitPersistentError($"Reload failed: {ex.Message}");
        }
    }

    public void SwitchProvider(string providerName)
    {
        var entry = Phi.Providers.ProviderCatalog.All.FirstOrDefault(
            p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            DeskLog.Write($"AvaloniaSlashActionSink.SwitchProvider: unknown '{providerName}'");
            _projector?.SubmitPersistentError(
                $"Unknown provider '{providerName}'. Use /connect via the model picker.");
            return;
        }
        if (!_providers.HasApiKey(entry))
        {
            _projector?.SubmitPersistentError(
                $"No API key for {entry.Name}. Open the model picker (left of the send button) to set one.");
            return;
        }
        var apiKey = _providers.GetApiKey(entry);
        var model = _providers.ResolveDefaultModel(entry);
        var provider = ProviderManager.CreateProvider(entry, apiKey);
        _session.SwitchProvider(provider, entry.Name, model);
        _providers.SaveDefault(entry, model);
        DeskLog.Write($"AvaloniaSlashActionSink.SwitchProvider: switched to {entry.Name}/{model}");
    }

    public void Quit()
    {
        // Classic desktop: ask the lifetime to shut down (graceful — fires
        // Window.Closing events). Single-view lifetimes (browser / mobile)
        // don't expose Shutdown(); fall back to process exit so the user
        // intent is honoured even in a future platform.
        var lifetime = Application.Current?.ApplicationLifetime;
        switch (lifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.Shutdown();
                break;
            case IControlledApplicationLifetime controlled:
                controlled.Shutdown();
                break;
            default:
                Environment.Exit(0);
                break;
        }
    }

    /// <summary>
    /// Sessions on the desktop are reachable via the SideMenu's sessions
    /// list. Surface that hint instead of opening a dialog so the user
    /// keeps using the visible affordance.
    /// </summary>
    public string? OpenSessionsDialogIfSupported() =>
        "Pick a session in the left sidebar to resume (or click \u25cb New chat).";

    /// <summary>
    /// Provider connect lives in the model picker footer combo: every
    /// provider's models are listed there, grouped by provider, with
    /// "(no key)" rows redacting keyless entries. Surface that hint.
    /// </summary>
    public string? OpenConnectDialogIfSupported() =>
        "Open the model picker (next to the send button) to connect or switch providers.";

    /// <summary>Same picker; one affordance covers both /connect and /models.</summary>
    public string? OpenModelsDialogIfSupported() =>
        "Use the model picker (next to the send button) to switch models.";

    private async Task NavigateToNewAsync()
    {
        try
        {
            var next = await _session.NewSessionAsync();
            _active.Replace(next);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"AvaloniaSlashActionSink.NavigateToNewAsync: threw: {ex}");
        }
    }
}
