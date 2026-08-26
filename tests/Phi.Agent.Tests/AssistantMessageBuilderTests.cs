namespace Phi.Agent.Tests;

public class AssistantMessageBuilderTests
{
    [Test]
    public async Task AdoptFinal_FoldsTerminalMetadataOntoPartial()
    {
        var partial = new AssistantMessage
        {
            Model = "streamed-model",
            Content = [new TextBlock("hello")],
        };
        var final = new AssistantMessage
        {
            Api = "anthropic-messages",
            Provider = "anthropic",
            Model = "server-model",
            StopReason = StopReasons.ToolUse,
            Usage = new Usage { Input = 10, Output = 20, TotalTokens = 30 },
            ResponseModel = "server-model-v2",
            ResponseProvider = "anthropic",
            ResponseId = "resp_123",
            Content = [new TextBlock("reordered by terminal build")],
        };

        var adopted = AssistantMessageBuilder.AdoptFinal(partial, final);

        await Assert.That(adopted.Api).IsEqualTo("anthropic-messages");
        await Assert.That(adopted.Provider).IsEqualTo("anthropic");
        await Assert.That(adopted.StopReason).IsEqualTo(StopReasons.ToolUse);
        await Assert.That(adopted.Usage.Input).IsEqualTo(10);
        await Assert.That(adopted.Model).IsEqualTo("server-model");
        await Assert.That(adopted.ResponseModel).IsEqualTo("server-model-v2");
        await Assert.That(adopted.ResponseProvider).IsEqualTo("anthropic");
        await Assert.That(adopted.ResponseId).IsEqualTo("resp_123");
    }

    [Test]
    public async Task AdoptFinal_EmptyTerminalIdentity_KeepsPartialValues()
    {
        var partial = new AssistantMessage
        {
            Api = "partial-api",
            Provider = "partial-provider",
        };
        var final = new AssistantMessage { Api = "", Provider = "" };

        var adopted = AssistantMessageBuilder.AdoptFinal(partial, final);

        await Assert.That(adopted.Api).IsEqualTo("partial-api");
        await Assert.That(adopted.Provider).IsEqualTo("partial-provider");
    }

    [Test]
    public async Task AdoptFinal_KeepsStreamedOrderContent()
    {
        // The terminal build may reorder blocks (Anthropic prepends
        // thinking); the streamed-order partial stays authoritative.
        var partial = new AssistantMessage
        {
            Content = [new TextBlock("first"), new ThinkingBlock("thought")],
        };
        var final = new AssistantMessage
        {
            Content = [new ThinkingBlock("thought"), new TextBlock("first")],
        };

        var adopted = AssistantMessageBuilder.AdoptFinal(partial, final);

        await Assert.That(adopted.Content.Count).IsEqualTo(2);
        await Assert.That(adopted.Content[0]).IsTypeOf<TextBlock>();
        await Assert.That(adopted.Content[1]).IsTypeOf<ThinkingBlock>();
    }

    [Test]
    public async Task AdoptFinal_EmptyTerminalModel_KeepsPartialModel()
    {
        var partial = new AssistantMessage { Model = "streamed-model" };
        var final = new AssistantMessage { Model = "" };

        var adopted = AssistantMessageBuilder.AdoptFinal(partial, final);

        await Assert.That(adopted.Model).IsEqualTo("streamed-model");
    }

    // ──────── Apply: folding granular provider events ────────

    [Test]
    public async Task Apply_TextDelta_AppendsToTrailingTextBlock()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new TextDeltaEvent("hello "));
        partial = AssistantMessageBuilder.Apply(partial, new TextDeltaEvent("world"));

        await Assert.That(partial.Content.Count).IsEqualTo(1);
        await Assert.That(((TextBlock)partial.Content[0]).Text).IsEqualTo("hello world");
    }

    [Test]
    public async Task Apply_TextDelta_AfterThinking_OpensNewTextBlock()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingDeltaEvent("thinking"));
        partial = AssistantMessageBuilder.Apply(partial, new TextDeltaEvent("answer"));

        await Assert.That(partial.Content.Count).IsEqualTo(2);
        await Assert.That(partial.Content[0]).IsTypeOf<ThinkingBlock>();
        await Assert.That(((TextBlock)partial.Content[1]).Text).IsEqualTo("answer");
    }

    [Test]
    public async Task Apply_ThinkingDelta_FirstDelta_OpensThinkingBlockLazily()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingDeltaEvent("reason"));

        await Assert.That(partial.Content.Count).IsEqualTo(1);
        await Assert.That(((ThinkingBlock)partial.Content[0]).Thinking).IsEqualTo("reason");
    }

    [Test]
    public async Task Apply_ThinkingDelta_AppendsToTrailingThinkingBlock()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingDeltaEvent("reason "));
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingDeltaEvent("more"));

        await Assert.That(partial.Content.Count).IsEqualTo(1);
        await Assert.That(((ThinkingBlock)partial.Content[0]).Thinking).IsEqualTo("reason more");
    }

    [Test]
    public async Task Apply_ThinkingEnd_StampsSignatureOntoTrailingThinkingBlock()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingDeltaEvent("reason"));
        partial = AssistantMessageBuilder.Apply(
            partial, new ThinkingEndEvent(new ThinkingBlock("") { ThinkingSignature = "sig-123" }));

        await Assert.That(((ThinkingBlock)partial.Content[0]).ThinkingSignature).IsEqualTo("sig-123");
    }

    [Test]
    public async Task Apply_ThinkingEnd_WithNullSignature_IsNoOp()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingDeltaEvent("reason"));
        partial = AssistantMessageBuilder.Apply(partial, new ThinkingEndEvent(new ThinkingBlock("")));

        await Assert.That(((ThinkingBlock)partial.Content[0]).ThinkingSignature).IsNull();
    }

    [Test]
    public async Task Apply_ThinkingEnd_WithoutOpenThinkingBlock_IsNoOp()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(
            partial, new ThinkingEndEvent(new ThinkingBlock("") { ThinkingSignature = "sig" }));

        await Assert.That(partial.Content.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_ToolCallEvent_AppendsToolCall()
    {
        var partial = new AssistantMessage();
        partial = AssistantMessageBuilder.Apply(partial, new TextDeltaEvent("let me check"));
        partial = AssistantMessageBuilder.Apply(partial, new ToolCallEvent(new ToolCall("c1", "bash")));

        await Assert.That(partial.Content.Count).IsEqualTo(2);
        await Assert.That(partial.Content[1]).IsTypeOf<ToolCall>();
        await Assert.That(partial.ToolCalls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_UnrecognizedEvent_IsNoOp()
    {
        var partial = new AssistantMessage { Content = [new TextBlock("hi")] };
        var result = AssistantMessageBuilder.Apply(partial, new AssistantStartEvent());

        await Assert.That(result.Content.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_EmptyDelta_IsSkipped()
    {
        var partial = new AssistantMessage();
        var result = AssistantMessageBuilder.Apply(partial, new TextDeltaEvent(""));

        await Assert.That(result.Content.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_DoesNotMutateInputPartial()
    {
        var partial = new AssistantMessage { Content = [new TextBlock("original")] };
        _ = AssistantMessageBuilder.Apply(partial, new TextDeltaEvent("append"));

        await Assert.That(partial.Content.Count).IsEqualTo(1);
        await Assert.That(((TextBlock)partial.Content[0]).Text).IsEqualTo("original");
    }

    // ──────── MapFinishReason ────────

    [Test]
    public async Task MapFinishReason_RecognizesToolUseVariants()
    {
        await Assert.That(AssistantMessageBuilder.MapFinishReason("tool_calls")).IsEqualTo(StopReasons.ToolUse);
        await Assert.That(AssistantMessageBuilder.MapFinishReason("tool_use")).IsEqualTo(StopReasons.ToolUse);
        await Assert.That(AssistantMessageBuilder.MapFinishReason("toolUse")).IsEqualTo(StopReasons.ToolUse);
    }

    [Test]
    public async Task MapFinishReason_RecognizesLengthVariants()
    {
        await Assert.That(AssistantMessageBuilder.MapFinishReason("length")).IsEqualTo(StopReasons.Length);
        await Assert.That(AssistantMessageBuilder.MapFinishReason("max_tokens")).IsEqualTo(StopReasons.Length);
        await Assert.That(AssistantMessageBuilder.MapFinishReason("incomplete")).IsEqualTo(StopReasons.Length);
    }

    [Test]
    public async Task MapFinishReason_UnknownFallsThroughToStop()
    {
        await Assert.That(AssistantMessageBuilder.MapFinishReason("stop")).IsEqualTo(StopReasons.Stop);
        await Assert.That(AssistantMessageBuilder.MapFinishReason("end_turn")).IsEqualTo(StopReasons.Stop);
        await Assert.That(AssistantMessageBuilder.MapFinishReason(null)).IsEqualTo(StopReasons.Stop);
    }
}
