namespace Phi.Slash;

/// <summary>
/// Uniform slash-command dispatch for every Phi host (TUI, Avalonia desk,
/// future shells). The matching / completion rules live in
/// <see cref="SlashCommands"/>; this class drives them against a sink so
/// the same <c>/new</c> / <c>/skill:NAME</c> / <c>/reload</c> sequence
/// behaves identically regardless of shell. Each host supplies its own
/// <see cref="ISlashActionSink"/>; the dispatcher itself never touches
/// terminal, Avalonia, dialogs, or UI frameworks.
/// </summary>
public static class SlashInputDispatcher
{
    /// <summary>
    /// Dispatches a non-empty <paramref name="text"/> from the prompt input.
    /// Steers / submits / load-skill / runs a built-in command / runs an
    /// extension-registered command — in that order of fallback. Returns
    /// <see cref="SlashDispatchOutcome.None"/> when the caller should treat
    /// the input as a no-op (currently unreachable: empty text is filtered
    /// upstream), or a transient / error message to surface in the chat
    /// transcript when no fallback applies.
    /// </summary>
    /// <param name="isRunning">
    /// Whether the active session is currently running a turn. The dispatcher
    /// steers the message (rather than submitting it as a prompt) when this is
    /// true; the host supplies the value from its session.
    /// </param>
    /// <param name="extensionDispatcher">
    /// Optional closure the dispatcher invokes for extension-registered
    /// commands (<c>api.RegisterCommand</c>). Receives
    /// <c>(name, args, isRunning)</c>; the host surfaces the returned
    /// non-null string as a transient in the chat transcript. Null in hosts
    /// without an extension runtime.
    /// </param>
    public static SlashDispatchOutcome Dispatch(
        string text,
        IReadOnlyList<SlashCommandDef> commands,
        bool isRunning,
        Func<string, string, bool, string?>? extensionDispatcher,
        ISlashActionSink sink)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(sink);

        var trimmed = text.Trim();

        // 1) Built-in / catalogued commands — exact match first.
        if (SlashCommands.Match(trimmed) is { } command)
        {
            return InvokeBuiltInExact(command, sink);
        }

        // 2) Extension-registered commands: split name + args, then check
        //    the merged list. The registry's TryDispatch swallows handler
        //    failures and returns a transient message; surface it.
        if (TrySplitExtensionCommand(trimmed, out var extName, out var extArgs)
            && commands.Any(c => c.Name.TrimStart('/').Equals(
                extName, StringComparison.OrdinalIgnoreCase))
            && extensionDispatcher is not null)
        {
            var msg = extensionDispatcher(extName, extArgs, isRunning);
            if (msg is not null) return SlashDispatchOutcome.Transient(msg);
            return SlashDispatchOutcome.None;
        }

        // 3) /skill:NAME [prompt] — fanned-out to its own matcher.
        if (SlashCommands.MatchSkill(trimmed) is { } skill)
        {
            return SlashDispatchOutcome.LoadSkill(skill.SkillName, skill.Prompt);
        }

        // 4) /connect <provider> — provider-switch convenience (TUI uses
        //    dialog when bare; Avalonia disables bare usage). Implementation
        //    lives in the host's sink via SwitchProvider.
        if (SlashCommands.MatchWithArgs(trimmed) is { } withArgs
            && string.Equals(withArgs.Command, "/connect", StringComparison.OrdinalIgnoreCase))
        {
            sink.SwitchProvider(withArgs.Args);
            return SlashDispatchOutcome.None;
        }

        // 5) Not a slash command → submit as prompt (or steer when a turn
        //    is running, per the host's running-state semantics).
        if (isRunning)
        {
            sink.EnqueueSteering(trimmed);
            return SlashDispatchOutcome.None;
        }

        sink.SubmitPrompt(trimmed);
        return SlashDispatchOutcome.None;
    }

    private static SlashDispatchOutcome InvokeBuiltInExact(
        string command, ISlashActionSink sink)
    {
        switch (command)
        {
            case "/new":
                sink.NavigateToNew();
                return SlashDispatchOutcome.None;
            case "/reload":
                sink.ReloadExtensions();
                return SlashDispatchOutcome.None;
            case "/exit":
                sink.Quit();
                return SlashDispatchOutcome.None;
            case "/sessions":
                var sessionsMsg = sink.OpenSessionsDialogIfSupported();
                return sessionsMsg is null
                    ? SlashDispatchOutcome.None
                    : SlashDispatchOutcome.Transient(sessionsMsg);
            case "/connect":
                var connectMsg = sink.OpenConnectDialogIfSupported();
                return connectMsg is null
                    ? SlashDispatchOutcome.None
                    : SlashDispatchOutcome.Transient(connectMsg);
            case "/models":
                var modelsMsg = sink.OpenModelsDialogIfSupported();
                return modelsMsg is null
                    ? SlashDispatchOutcome.None
                    : SlashDispatchOutcome.Transient(modelsMsg);
            default:
                // Catalog includes other names today (defensive fallback);
                // treat as not-a-command so the input goes to the prompt.
                return new SlashDispatchOutcome(SlashDispatchKind.SubmitPrompt, null);
        }
    }

    /// <summary>
    /// Splits a slash input into (canonicalName, trailingArgs). The name is
    /// the first whitespace-separated token with any leading <c>/</c>
    /// stripped; the args are everything after the first whitespace
    /// (trimmed). Returns false for inputs that don't start with <c>/</c>
    /// or are empty after the trim.
    /// </summary>
    private static bool TrySplitExtensionCommand(
        string text, out string name, out string args)
    {
        name = "";
        args = "";
        if (text.Length == 0 || text[0] != '/') return false;

        var firstSpace = text.IndexOf(' ');
        if (firstSpace < 0)
        {
            name = text.TrimStart('/');
            return name.Length > 0;
        }
        name = text[..firstSpace].TrimStart('/');
        if (name.Length == 0) return false;
        args = text[(firstSpace + 1)..].Trim();
        return true;
    }
}

/// <summary>
/// Result of <see cref="SlashInputDispatcher.Dispatch"/>. Hosts render the
/// <see cref="Message"/> as a transient (info / error) in the transcript
/// when non-null; <see cref="Kind"/> indicates which side-effect to apply.
/// </summary>
public enum SlashDispatchKind
{
    /// <summary>No side-effect reported; the sink already ran the action.</summary>
    None,
    /// <summary>Show a transient message in the transcript.</summary>
    Transient,
    /// <summary>Load a skill via <see cref="ISession.LoadSkillAsync"/>.</summary>
    LoadSkill,
    /// <summary>Submit the trimmed text as a regular prompt (fallback).</summary>
    SubmitPrompt,
}

public readonly record struct SlashDispatchOutcome(
    SlashDispatchKind Kind,
    string? Message)
{
    public static readonly SlashDispatchOutcome None =
        new(SlashDispatchKind.None, null);
    public static SlashDispatchOutcome Transient(string? message) =>
        new(SlashDispatchKind.Transient, message);
    public static SlashDispatchOutcome LoadSkill(string name, string? prompt) =>
        new(SlashDispatchKind.LoadSkill, null)
        {
            SkillName = name,
            SkillPrompt = prompt,
        };
    public string? SkillName { get; init; }
    public string? SkillPrompt { get; init; }
}

/// <summary>
/// Host-implemented side-effects for slash commands. The dispatcher never
/// touches terminal / Avalonia / dialogs / state directly; each UI maps
/// these calls onto its existing primitives (e.g. Avalonia's
/// <c>ActiveSession.Replace(next)</c>, TUI's dialog shower). The three
/// <c>Open*DialogIfSupported</c> methods return <c>null</c> for hosts that
/// don't carry an in-app dialog (Avalonia surfaces pickers in the input
/// chrome instead); a non-null string surfaces as a transient guidance
/// message. <see cref="ReloadExtensions"/> /
/// <see cref="NavigateToNew"/> / <see cref="SwitchProvider"/> /
/// <see cref="LoadSkillAsync"/> (well, load-skill stays in
/// <see cref="SlashDispatchKind.LoadSkill"/>) are always supported.
/// </summary>
public interface ISlashActionSink
{
    void SubmitPrompt(string text);
    void EnqueueSteering(string text);
    void NavigateToNew();
    void ReloadExtensions();
    void SwitchProvider(string providerName);
    void Quit();

    /// <summary><c>null</c> when the host surfaces sessions via its own UI.</summary>
    string? OpenSessionsDialogIfSupported();

    /// <summary><c>null</c> when the host surfaces provider connect via its own UI.</summary>
    string? OpenConnectDialogIfSupported();

    /// <summary><c>null</c> when the host surfaces model switching via its own UI.</summary>
    string? OpenModelsDialogIfSupported();
}
