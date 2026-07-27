using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PhiAgent.Tests;

public class UserMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task Serialize_StringContent_ProducesBareStringInWire()
    {
        var msg = new UserMessage { Content = "hello", Timestamp = 1_700_000_000_000 };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).IsEqualTo(
            """{"role":"user","content":"hello","timestamp":1700000000000}""");
    }

    [Test]
    public async Task Serialize_BlocksContent_ProducesPolymorphicArrayInWire()
    {
        var msg = new UserMessage
        {
            Content = new BlocksUserContent([
                new TextBlock("hi"),
                new ImageBlock("base64data", "image/png"),
            ]),
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).IsEqualTo(
            """{"role":"user","content":[{"type":"text","text":"hi"},{"type":"image","data":"base64data","mimeType":"image/png"}],"timestamp":1700000000000}""");
    }

    [Test]
    public async Task Deserialize_BareStringContent_ProducesTextUserContent()
    {
        const string json = """{"role":"user","content":"hello","timestamp":1700000000000}""";

        var msg = JsonSerializer.Deserialize<UserMessage>(json, Options);

        await Assert.That(msg).IsNotNull();
        await Assert.That(msg!.Content).IsTypeOf<TextUserContent>();
        await Assert.That(((TextUserContent)msg.Content).Text).IsEqualTo("hello");
    }

    [Test]
    public async Task Deserialize_ArrayContent_DispatchesPolymorphicBlocks()
    {
        const string json = """
            {
              "role": "user",
              "content": [
                {"type":"text","text":"hi"},
                {"type":"image","data":"abc","mimeType":"image/png"}
              ],
              "timestamp": 1700000000000
            }
            """;

        var msg = JsonSerializer.Deserialize<UserMessage>(json, Options);

        await Assert.That(msg).IsNotNull();
        var blocks = (BlocksUserContent)msg!.Content;
        await Assert.That(blocks.Blocks.Count).IsEqualTo(2);
        await Assert.That(blocks.Blocks[0]).IsTypeOf<TextBlock>();
        await Assert.That(blocks.Blocks[1]).IsTypeOf<ImageBlock>();
    }

    [Test]
    public async Task RoundTrip_StringContent_ProducesStableJson()
    {
        var original = new UserMessage { Content = "hello", Timestamp = 1_700_000_000_000 };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<UserMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
    }

    [Test]
    public async Task RoundTrip_BlocksContent_ProducesStableJson()
    {
        var original = new UserMessage
        {
            Content = new BlocksUserContent([
                new TextBlock("hi") { TextSignature = "sig-abc" },
                new ImageBlock("data", "image/png"),
            ]),
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<UserMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
    }

    [Test]
    public async Task TextBlock_WithSignature_AppearsInUserMessageWire()
    {
        var msg = new UserMessage
        {
            Content = new BlocksUserContent([
                new TextBlock("hi") { TextSignature = "EqQBCkg..." },
            ]),
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).IsEqualTo(
            """{"role":"user","content":[{"type":"text","text":"hi","textSignature":"EqQBCkg..."}],"timestamp":1700000000000}""");
    }

    [Test]
    public async Task TextBlock_WithoutSignature_FieldIsOmittedInUserMessageWire()
    {
        var msg = new UserMessage
        {
            Content = new BlocksUserContent([new TextBlock("hi")]),
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).IsEqualTo(
            """{"role":"user","content":[{"type":"text","text":"hi"}],"timestamp":1700000000000}""");
    }

    [Test]
    public async Task Deserialize_UnknownRoleField_IsIgnored()
    {
        const string json = """{"role":"user","content":"hi","timestamp":1,"unknownField":"x"}""";

        var msg = JsonSerializer.Deserialize<UserMessage>(json, Options);

        await Assert.That(msg).IsNotNull();
    }
}

public class AssistantMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task Serialize_DefaultsProducesAllRequiredFields()
    {
        var msg = new AssistantMessage { Timestamp = 1_700_000_000_000 };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).IsEqualTo(
            """{"role":"assistant","content":[],"api":"unknown","provider":"unknown","model":"unknown","usage":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"totalTokens":0,"cost":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"total":0}},"stopReason":"stop","timestamp":1700000000000}""");
    }

    [Test]
    public async Task Serialize_TextAndToolCall_PolymorphicDispatchWorks()
    {
        var msg = new AssistantMessage
        {
            Content = [
                new TextBlock("Let me check."),
                new ToolCall("call_1", "bash")
                {
                    Arguments = new JsonObject { ["command"] = "ls -la" },
                },
            ],
            StopReason = StopReasons.ToolUse,
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).IsEqualTo(
            """{"role":"assistant","content":[{"type":"text","text":"Let me check."},{"type":"toolCall","id":"call_1","name":"bash","arguments":{"command":"ls -la"}}],"api":"unknown","provider":"unknown","model":"unknown","usage":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"totalTokens":0,"cost":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"total":0}},"stopReason":"toolUse","timestamp":1700000000000}""");
    }

    [Test]
    public async Task Deserialize_ToolCallArguments_PreservesJsonStructure()
    {
        const string json = """
            {
              "role":"assistant",
              "content":[{"type":"toolCall","id":"c1","name":"fs_read","arguments":{"path":"/tmp/x","recursive":true,"opts":{"depth":2}}}],
              "api":"anthropic",
              "provider":"anthropic",
              "model":"claude-opus-4-7",
              "usage":{"input":10,"output":20,"cacheRead":0,"cacheWrite":0,"totalTokens":30,"cost":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"total":0}},
              "stopReason":"toolUse",
              "timestamp":1700000000000
            }
            """;

        var msg = JsonSerializer.Deserialize<AssistantMessage>(json, Options);

        await Assert.That(msg).IsNotNull();
        await Assert.That(msg!.ToolCalls.Count).IsEqualTo(1);
        var call = msg.ToolCalls[0];
        await Assert.That(call.Id).IsEqualTo("c1");
        await Assert.That(call.Name).IsEqualTo("fs_read");
        await Assert.That(call.Arguments["path"]!.GetValue<string>()).IsEqualTo("/tmp/x");
        await Assert.That(call.Arguments["recursive"]!.GetValue<bool>()).IsTrue();
        await Assert.That(call.Arguments["opts"]!["depth"]!.GetValue<int>()).IsEqualTo(2);
    }

    [Test]
    public async Task ThinkingBlock_RoundTripsWithSignature()
    {
        var msg = new AssistantMessage
        {
            Content = [new ThinkingBlock("reasoning...") { ThinkingSignature = "sig-xyz" }],
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(msg, Options);
        var roundTripped = JsonSerializer.Deserialize<AssistantMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(firstJson).Contains("\"type\":\"thinking\"");
        await Assert.That(firstJson).Contains("\"thinkingSignature\":\"sig-xyz\"");
        await Assert.That(secondJson).IsEqualTo(firstJson);
    }

    [Test]
    public async Task Usage_CacheWrite1H_UsesCapitalHInWire()
    {
        var usage = new Usage
        {
            Input = 100,
            Output = 200,
            CacheRead = 50,
            CacheWrite = 25,
            CacheWrite1h = 10,
            TotalTokens = 350,
        };

        var json = JsonSerializer.Serialize(usage, Options);

        await Assert.That(json).Contains("\"cacheWrite1H\":10");
    }

    [Test]
    public async Task Text_ConcatenatesOnlyTextBlocksInOrder()
    {
        var msg = new AssistantMessage
        {
            Content = [
                new TextBlock("hello "),
                new ThinkingBlock("inner thought"),
                new TextBlock("world"),
                new ToolCall("c1", "noop"),
            ],
        };

        await Assert.That(msg.Text).IsEqualTo("hello world");
        await Assert.That(msg.ThinkingText).IsEqualTo("inner thought");
        await Assert.That(msg.ToolCalls.Count).IsEqualTo(1);
    }
}

public class ToolResultMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task RoundTrip_TextAndImage_ProducesStableJson()
    {
        var original = new ToolResultMessage
        {
            ToolCallId = "call_1",
            ToolName = "bash",
            Content = [
                new TextBlock("hello"),
                new ImageBlock("base64", "image/png"),
            ],
            IsError = false,
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<ToolResultMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
        await Assert.That(roundTripped!.Text).IsEqualTo("hello");
    }

    [Test]
    public async Task Serialize_IsErrorTrue_AppearsInWire()
    {
        var msg = new ToolResultMessage
        {
            ToolCallId = "call_1",
            ToolName = "bash",
            IsError = true,
            Content = [new TextBlock("command failed")],
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).Contains("\"isError\":true");
    }

    [Test]
    public async Task Serialize_NullFields_AreOmitted()
    {
        var msg = new ToolResultMessage
        {
            ToolCallId = "c",
            ToolName = "n",
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).DoesNotContain("\"details\"");
        await Assert.That(json).DoesNotContain("\"addedToolNames\"");
    }

    [Test]
    public async Task Serialize_Details_PreservesArbitraryJson()
    {
        var msg = new ToolResultMessage
        {
            ToolCallId = "c",
            ToolName = "n",
            Content = [new TextBlock("ok")],
            Details = JsonNode.Parse("""{"exitCode":0,"duration":1.5,"nested":{"k":"v"}}"""),
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);
        var roundTripped = JsonSerializer.Deserialize<ToolResultMessage>(json, Options);

        await Assert.That(json).Contains("\"details\":{\"exitCode\":0,\"duration\":1.5,\"nested\":{\"k\":\"v\"}}");
        await Assert.That(roundTripped!.Details).IsNotNull();
        await Assert.That(roundTripped.Details!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task Serialize_AddedToolNames_AppearsInWire()
    {
        var msg = new ToolResultMessage
        {
            ToolCallId = "c",
            ToolName = "n",
            AddedToolNames = ["foo", "bar"],
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).Contains("\"addedToolNames\":[\"foo\",\"bar\"]");
    }
}

public class BashExecutionMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task RoundTrip_FullPayload_ProducesStableJson()
    {
        var original = new BashExecutionMessage
        {
            Command = "ls -la",
            Output = "file1\nfile2\n",
            ExitCode = 0,
            Cancelled = false,
            Truncated = false,
            FullOutputPath = "/tmp/output.log",
            Timestamp = 1_700_000_000_000,
            ExcludeFromContext = false,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<BashExecutionMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
    }

    [Test]
    public async Task Serialize_MinimalPayload_OmitsNullOptionalFields()
    {
        var msg = new BashExecutionMessage
        {
            Command = "ls",
            Output = "",
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).DoesNotContain("\"exitCode\"");
        await Assert.That(json).DoesNotContain("\"fullOutputPath\"");
        await Assert.That(json).IsEqualTo(
            """{"role":"bashExecution","command":"ls","output":"","cancelled":false,"truncated":false,"timestamp":1700000000000,"excludeFromContext":false}""");
    }
}

public class CustomMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task RoundTrip_StringContent_ProducesStableJson()
    {
        var original = new CustomMessage
        {
            CustomType = "session-marker",
            Content = "checkpoint 1",
            Display = true,
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<CustomMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
        await Assert.That(roundTripped!.Text).IsEqualTo("checkpoint 1");
    }

    [Test]
    public async Task RoundTrip_BlocksContent_ProducesStableJson()
    {
        var original = new CustomMessage
        {
            CustomType = "image-msg",
            Content = new BlocksUserContent([new ImageBlock("d", "image/png")]),
            Display = false,
            Details = JsonNode.Parse("""{"tag":"urgent"}"""),
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<CustomMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
        await Assert.That(roundTripped!.Display).IsFalse();
    }
}

public class BranchSummaryMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task RoundTrip_ProducesStableJson()
    {
        var original = new BranchSummaryMessage
        {
            Summary = "Branch ABC was merged",
            FromId = "session_xyz",
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<BranchSummaryMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
        await Assert.That(firstJson).Contains("\"fromId\":\"session_xyz\"");
    }

    [Test]
    public async Task Serialize_UsesCamelCaseRoleAndFromId()
    {
        var msg = new BranchSummaryMessage
        {
            Summary = "summary",
            FromId = "id-1",
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).Contains("\"role\":\"branchSummary\"");
        await Assert.That(json).Contains("\"fromId\":\"id-1\"");
    }
}

public class CompactionSummaryMessageTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Test]
    public async Task RoundTrip_ProducesStableJson()
    {
        var original = new CompactionSummaryMessage
        {
            Summary = "Compacted 5000 tokens into 200",
            TokensBefore = 5000,
            Timestamp = 1_700_000_000_000,
        };

        var firstJson = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<CompactionSummaryMessage>(firstJson, Options);
        var secondJson = JsonSerializer.Serialize(roundTripped, Options);

        await Assert.That(secondJson).IsEqualTo(firstJson);
    }

    [Test]
    public async Task Serialize_TokensBeforeAppearsInWire()
    {
        var msg = new CompactionSummaryMessage
        {
            Summary = "x",
            TokensBefore = 12345,
            Timestamp = 1_700_000_000_000,
        };

        var json = JsonSerializer.Serialize(msg, Options);

        await Assert.That(json).Contains("\"tokensBefore\":12345");
    }
}
