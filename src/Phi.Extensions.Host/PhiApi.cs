using Phi.Agent;
using Phi.Extensions.Events;

namespace Phi.Extensions.Host;

/// <summary>
/// Host-side implementation of <see cref="IPhiApi"/>. One instance per
/// extension per session, constructed by
/// <see cref="ExtensionRuntime.Initialize"/>.
/// <para>
/// Sprint 1 implements:
/// <list type="bullet">
/// <item><see cref="Name"/> / <see cref="Version"/> / <see cref="Context"/>
/// — projections from the loaded extension + session + bridge.</item>
/// <item><see cref="RegisterTool"/> — folds into the live harness via
/// <c>ExtensionRuntime.RegisterTool</c>.</item>
/// <item><see cref="AddPromptGuideline"/> — appends to the live system prompt.</item>
/// <item><see cref="Notify"/> — forwards to the UI bridge.</item>
/// </list>
/// Sprint 2+ fills in: <see cref="RegisterCommand"/>, <see cref="On(string, Action{PhiEvent, IPhiContext})"/>,
/// <see cref="SubmitUserMessage"/> / <see cref="SubmitCustomMessage"/>,
/// <see cref="SubmitTranscriptLine"/>, <see cref="SwitchModel"/>, <see cref="SwitchProvider"/>,
/// <see cref="AppendEntryAsync"/>, renderers.
/// </para>
/// </summary>
internal sealed class PhiApi : IPhiApi
{
    private readonly ExtensionRuntime _runtime;
    private readonly LoadedExtension _extension;
    private readonly ExtensionGeneration _generation;

    public PhiApi(
        ExtensionRuntime runtime,
        LoadedExtension extension,
        IPhiContext context,
        ExtensionGeneration generation)
    {
        _runtime = runtime;
        _extension = extension;
        Context = context;
        _generation = generation;
    }

    /// <summary>Guards every action method against a stale generation.</summary>
    private void AssertAlive() => _generation.AssertAlive();

    /// <summary>
    /// Gate every action method that touches a host resource. Looks up
    /// the method's required <see cref="ExtensionCapability"/> via
    /// <see cref="CapabilityActionMap"/>; if the extension didn't include
    /// it in <see cref="PhiExtensionAttribute.Capabilities"/>, either
    /// log the mismatch (v1 transparent) or throw
    /// <see cref="ExtensionError"/> (v1.5 strict, controlled by
    /// <see cref="ExtensionRuntime.CapabilityEnforcement"/>). Both
    /// branches write a JSONL record to <c>~/.phi/audit.log</c>.
    /// </summary>
    private void EnforceCapability(string methodName)
    {
        AssertAlive();
        var required = CapabilityActionMap.RequiredFor(methodName);
        if (required is null) return; // registration / identity — no cap required
        var declared = _extension.DeclaredCapabilities;
        if ((declared & required) == required) return; // declared — proceed

        switch (_runtime.CapabilityEnforcement)
        {
            case CapabilityEnforcementMode.Transparent:
                AuditLogger.Write(AuditEvent.CapabilityMismatch(
                    _extension.Name, methodName, required.Value, declared));
                break;
            case CapabilityEnforcementMode.Strict:
                AuditLogger.Write(AuditEvent.CapabilityBlocked(
                    _extension.Name, methodName, required.Value, declared));
                throw new ExtensionError(
                    $"extension '{_extension.Name}' invoked {methodName}() without declaring " +
                    $"{required}; add it to [PhiExtension(Capabilities = ...)] or set " +
                    $"Phi.Extensions.Host.CapabilityEnforcement = Transparent to allow with a warning.");
        }
    }

    // ──────── Identity ────────

    public string Name => _extension.Name;
    public string Version => _extension.Version;
    public IPhiContext Context { get; }

    // ──────── Registration ────────

    public void RegisterTool(Tool tool, ToolContribution? contribution = null)
    {
        AssertAlive();
        ArgumentNullException.ThrowIfNull(tool);
        // Append directly to the live harness so the next tool call picks it up.
        _runtime.RegisterTool(_extension, tool, contribution);
    }

    public void RegisterCommand(
        string name,
        PhiCommandHandler handler,
        string description = "",
        string usage = "",
        IReadOnlyList<string>? aliases = null)
    {
        AssertAlive();
        _runtime.RegisterCommand(_extension, name, handler, description, aliases);
    }

    public void AddPromptGuideline(string guideline)
    {
        AssertAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(guideline);
        _runtime.AddPromptGuideline(_extension, guideline);
    }

    public void RegisterToolCard(
        string toolName,
        ToolDescriptor descriptor,
        Rendering.ToolCardRenderer? renderer = null)
    {
        AssertAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(descriptor);
        _runtime.RegisterToolCard(_extension, toolName, descriptor, renderer);
    }

    public void RegisterTranscriptLineRenderer(
        string lineType,
        Rendering.TranscriptLineRenderer renderer)
    {
        AssertAlive();
        _runtime.RegisterTranscriptLineRenderer(_extension, lineType, renderer);
    }

    public void RegisterMessageRenderer(
        string customType,
        Rendering.MessageRenderer renderer)
        => throw new NotImplementedException("RegisterMessageRenderer lands in Sprint 4 (custom-typed assistant messages).");

    // ──────── Events ────────

    public IDisposable On(string eventName, Func<PhiEvent, IPhiContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        // Sprint 2: route through HookDispatch / EventDispatch against
        // ISession.StateChanged + ISession.HarnessEvent. Sprint 1 just
        // records the subscription so reload-diagnostics show what extensions
        // expect, and returns a no-op IDisposable.
        return _runtime.SubscribeEvent(_extension, eventName, handler);
    }

    public IDisposable On(string eventName, Action<PhiEvent, IPhiContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return On(eventName, (e, c) =>
        {
            handler(e, c);
            return ValueTask.CompletedTask;
        });
    }

    // ──────── Actions ────────

    public void SubmitUserMessage(string text, MessageDelivery delivery = MessageDelivery.FollowUp)
    {
        EnforceCapability(nameof(SubmitUserMessage));
        AssertAlive();
        ArgumentNullException.ThrowIfNull(text);
        var msg = new UserMessage { Content = text };
        if (delivery == MessageDelivery.Steer) _runtime.Session.EnqueueSteering(msg);
        else _runtime.Session.EnqueueFollowUp(msg);
    }

    public void SubmitCustomMessage(
        string text,
        string customType,
        IReadOnlyDictionary<string, object?>? details = null,
        MessageDelivery delivery = MessageDelivery.FollowUp,
        bool triggerTurn = true)
    {
        EnforceCapability(nameof(SubmitCustomMessage));
        throw new NotImplementedException("SubmitCustomMessage lands in Sprint 4 (custom transcript lines + renderer).");
    }

    public void SubmitTranscriptLine(TranscriptLine line)
    {
        EnforceCapability(nameof(SubmitTranscriptLine));
        AssertAlive();
        _runtime.SubmitTranscriptLine(line);
    }

    public Task AppendEntryAsync(string ns, IReadOnlyDictionary<string, object?> data)
        => throw new NotImplementedException("AppendEntryAsync lands in Sprint 2 (persistence pipeline).");

    public void Notify(string message, NotifyLevel level = NotifyLevel.Info)
    {
        EnforceCapability(nameof(Notify));
        AssertAlive();
        _runtime.UiBridge.Notify(message, level);
    }

    public void SwitchModel(string model)
        => throw new NotImplementedException("SwitchModel lands in Sprint 2 (model switch on session).");

    public void SwitchProvider(IPhiProvider provider, string providerName, string model)
        => throw new NotImplementedException("SwitchProvider lands in Sprint 2 (provider ownership transfer).");
}
