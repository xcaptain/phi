namespace Phi.Extensions;

/// <summary>
/// The single API surface extensions see. Every public method on this
/// interface is either:
/// <list type="bullet">
/// <item><b>Registration</b> — sync, called inside
/// <see cref="IPhiExtension.Setup"/>; mutates the host's internal
/// state (tool catalog, slash command list, event subscriptions, etc.).</item>
/// <item><b>Action</b> — may be called any time after <see cref="IPhiExtension.Setup"/>
/// returns, <em>but requires a bound session</em>. Calling an action
/// method before <c>session_start</c> (or after <c>session_shutdown</c>) throws
/// <see cref="ExtensionError"/>.</item>
/// </list>
/// <para>
/// The host's <c>Phi.Extensions.Host.PhiApi</c> (Sprint 1) is the sealed
/// implementation. The class is internal — extensions see only this
/// interface. This lets us evolve method signatures without breaking
/// compiled extensions.
/// </para>
/// </summary>
public interface IPhiApi
{
    // ──────── Identity ────────

    /// <summary>The extension's <see cref="PhiExtensionAttribute.Name"/>.</summary>
    string Name { get; }

    /// <summary>The extension's <see cref="PhiExtensionAttribute.Version"/>.</summary>
    string Version { get; }

    /// <summary>Read-only session projection.</summary>
    IPhiContext Context { get; }

    // ──────── Registration (sync, only inside Setup) ────────

    /// <summary>
    /// Register <paramref name="tool"/> as an executable capability for the
    /// agent. <paramref name="contribution"/> is the prompt-side metadata
    /// (snippet / guidelines / capabilities); pass <c>null</c> to let the
    /// host derive everything from <see cref="Phi.Agent.Tool.Description"/>.
    /// </summary>
    void RegisterTool(Phi.Agent.Tool tool, Phi.Agent.ToolContribution? contribution = null);

    /// <summary>
    /// Register a slash command. <paramref name="handler"/> is invoked on
    /// the submit thread (TUI / Avalonia); keep it short. Async work
    /// should be wrapped in <c>Task.Run</c> by the handler itself.
    /// </summary>
    void RegisterCommand(
        string name,
        PhiCommandHandler handler,
        string description = "",
        string usage = "",
        IReadOnlyList<string>? aliases = null);

    /// <summary>
    /// Append a behavioral rule to the system prompt's guideline section.
    /// Prefer this over <see cref="RegisterTool"/>'s <c>PromptGuidelines</c>
    /// when the rule applies to the agent in general rather than to a
    /// specific tool.
    /// </summary>
    void AddPromptGuideline(string guideline);

    /// <summary>
    /// Register a custom card layout for <paramref name="toolName"/>. The
    /// host uses <paramref name="descriptor"/> for icon / title / kind;
    /// <paramref name="renderer"/> is the body formatter. The card is
    /// visible in both TUI and Avalonia without further work.
    /// </summary>
    void RegisterToolCard(
        string toolName,
        Phi.Agent.ToolDescriptor descriptor,
        Rendering.ToolCardRenderer? renderer = null);

    /// <summary>
    /// Register a renderer for transcript lines of <paramref name="lineType"/>.
    /// The renderer converts a <see cref="TranscriptLine"/> into the
    /// host's chat-line representation; the host caches by <c>lineType</c>.
    /// </summary>
    void RegisterTranscriptLineRenderer(
        string lineType,
        Rendering.TranscriptLineRenderer renderer);

    /// <summary>
    /// Register a renderer for custom-typed assistant messages (those
    /// submitted via <see cref="SubmitCustomMessage"/>). Useful for
    /// extensions that emit structured content (progress bars, charts,
    /// etc.) rather than plain text.
    /// </summary>
    void RegisterMessageRenderer(
        string customType,
        Rendering.MessageRenderer renderer);

    // ──────── Event subscriptions ────────

    /// <summary>
    /// Asynchronous event subscription. Returned <see cref="IDisposable"/>
    /// unsubscribes when disposed (and on <c>/reload</c> via the
    /// generation guard). <paramref name="eventName"/> matches the
    /// <c>PhiEvent</c> runtime type name (e.g. <c>"AgentStartEvent"</c>,
    /// <c>"ToolCallHookEvent"</c>); wildcards are not supported in v1.
    /// </summary>
    IDisposable On(string eventName, Func<Events.PhiEvent, IPhiContext, ValueTask> handler);

    /// <summary>Synchronous variant of <see cref="On(string, Func{PhiEvent, IPhiContext, ValueTask})"/>.</summary>
    IDisposable On(string eventName, Action<Events.PhiEvent, IPhiContext> handler);

    // ──────── Actions (require a bound session) ────────

    /// <summary>
    /// Inject a user prompt into the session's queue. <paramref name="delivery"/>
    /// chooses between <see cref="MessageDelivery.Steer"/> (next iteration)
    /// and <see cref="MessageDelivery.FollowUp"/> (after current turn).
    /// </summary>
    void SubmitUserMessage(string text, MessageDelivery delivery = MessageDelivery.FollowUp);

    /// <summary>
    /// Inject a custom-typed assistant message. <paramref name="customType"/>
    /// drives <see cref="RegisterMessageRenderer"/>; <paramref name="triggerTurn"/>
    /// requests a follow-up agent turn after this message lands.
    /// </summary>
    void SubmitCustomMessage(
        string text,
        string customType,
        IReadOnlyDictionary<string, object?>? details = null,
        MessageDelivery delivery = MessageDelivery.FollowUp,
        bool triggerTurn = true);

    /// <summary>
    /// Inject a transcript line. Renders via the registered
    /// <see cref="RegisterTranscriptLineRenderer"/> for <c>line.Type</c>;
    /// falls back to plain text if no renderer exists.
    /// </summary>
    void SubmitTranscriptLine(TranscriptLine line);

    /// <summary>
    /// Append a <c>namespace</c>-namespaced entry to the session's
    /// persisted JSONL transcript. Entries live in their own namespace
    /// (<c>"multi-agent:state"</c>, <c>"my-ext:cache"</c>) and are
    /// replayed on session resume.
    /// </summary>
    Task AppendEntryAsync(string ns, IReadOnlyDictionary<string, object?> data);

    /// <summary>Show a transient notification.</summary>
    void Notify(string message, NotifyLevel level = NotifyLevel.Info);

    /// <summary>Switch the session's active model (next run only).</summary>
    void SwitchModel(string model);

    /// <summary>
    /// Switch the session's active provider. <paramref name="provider"/>
    /// ownership transfers to the session (it disposes the previous
    /// provider and releases the new one on session dispose).
    /// </summary>
    void SwitchProvider(Phi.Agent.IPhiProvider provider, string providerName, string model);
}

/// <summary>
/// Delegate invoked when a slash command fires (e.g. user types
/// <c>/foo arg1 arg2</c>). <see cref="IPhiContext"/> gives access to
/// session state; <c>null</c> result = silent success, non-null =
/// display as transient message.
/// </summary>
public delegate string? PhiCommandHandler(string args, IPhiContext context);
