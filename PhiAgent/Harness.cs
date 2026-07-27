namespace PhiAgent;

/// <summary>
/// The agent loop: turn a user prompt into a final assistant message, executing
/// any tool calls the model returns and feeding results back into the context.
/// Combines the inner loop from tau's <c>loop.py</c> with the orchestration shell
/// from <c>harness.py</c>; session management, event bus, and hooks come later.
/// </summary>
public sealed class Harness
{
    private readonly IPhiProvider _provider;
    private readonly IReadOnlyList<Tool> _tools;
    private readonly ToolExecutor _executeTool;
    private readonly string _model;
    private readonly string _system;
    private readonly List<IAgentMessage> _messages = new();

    public Harness(
        IPhiProvider provider,
        IReadOnlyList<Tool> tools,
        ToolExecutor executeTool,
        string model,
        string system = "")
    {
        _provider = provider;
        _tools = tools;
        _executeTool = executeTool;
        _model = model;
        _system = system;
    }

    public IReadOnlyList<IAgentMessage> Messages => _messages;

    public async Task<HarnessResult> RunAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        _messages.Add(new UserMessage { Content = userMessage });

        while (true)
        {
            var assistant = await StreamTurnAsync(cancellationToken);
            _messages.Add(assistant);

            if (assistant.ToolCalls.Count == 0)
                return new HarnessResult(assistant, _messages);

            foreach (var call in assistant.ToolCalls)
            {
                var result = await _executeTool(
                    call.Name, call.Id, call.Arguments, cancellationToken);

                _messages.Add(new ToolResultMessage
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = result.Content,
                    IsError = result.IsError,
                });
            }
        }
    }

    private async Task<AssistantMessage> StreamTurnAsync(CancellationToken ct)
    {
        AssistantMessage? final = null;
        ProviderErrorEvent? lastError = null;

        await foreach (var ev in _provider.StreamResponseAsync(
            _model, _system, _messages, _tools, ct))
        {
            switch (ev)
            {
                case ProviderResponseEndEvent end:
                    final = end.Message;
                    break;
                case ProviderErrorEvent err:
                    lastError = err;
                    break;
            }
        }

        if (final is null)
        {
            var detail = lastError is not null
                ? $" Last provider error: {lastError.Message}"
                : " Stream ended without a final response.";
            throw new InvalidOperationException(
                $"Provider produced no ProviderResponseEndEvent.{detail}");
        }

        return final;
    }
}

public sealed record HarnessResult(
    AssistantMessage FinalMessage,
    IReadOnlyList<IAgentMessage> Messages);