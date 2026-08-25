using Phi.Slash;

namespace Phi.Extensions.Host;

/// <summary>
/// UI-facing view of registered slash commands. Mutable: the
/// <see cref="ExtensionRuntime"/> implementation appends / replaces entries
/// when an extension calls <c>api.RegisterCommand</c>; the UI consults the
/// same instance for both completion (every entry's name/description/usage)
/// and dispatch (look up by name + invoke the handler).
/// <para>
/// Implementations must be cheap to query from the UI thread (called on every
/// keystroke during <c>/</c> completion) and safe to invoke concurrently
/// with the agent loop (dispatch happens on the submit path).
/// </para>
/// </summary>
public interface ISlashCommandRegistry
{
    /// <summary>
    /// Snapshot of every command currently registered, for the
    /// autocomplete strip. Returns the canonical command (no leading
    /// <c>/</c>) with the description / usage / aliases the extension
    /// supplied at registration time.
    /// </summary>
    IEnumerable<SlashCommandDef> AllCommands { get; }

    /// <summary>
    /// Looks up <paramref name="commandName"/> and invokes its handler if
    /// the registry recognises it. <paramref name="args"/> is the
    /// post-command text (already trimmed; empty when the user typed just
    /// the command). The handler receives the live <see cref="IPhiContext"/>
    /// so it can read session metadata, prompt the user via
    /// <c>ctx.Ui</c>, etc.
    /// <para>
    /// Returns <c>true</c> when the name was recognised and the handler
    /// ran. The caller should show the handler's returned string as a
    /// transient message (null / empty → silent success). Returns
    /// <c>false</c> when the name is unknown; the caller should fall back
    /// to its built-in switch (and, if that misses too, submit the input
    /// as a regular prompt to the agent).
    /// </para>
    /// <para>
    /// Errors thrown by the handler are swallowed and surfaced as a transient
    /// error message — a misbehaving extension must not break the prompt
    /// dispatch path or crash the session.
    /// </para>
    /// </summary>
    bool TryDispatch(string commandName, string args, IPhiContext context, out string? result);
}
