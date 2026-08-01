namespace PhiAgent.Tests;

public class SessionEntryCodecTests
{
    [Test]
    public async Task RoundTrip_UserEntry_PreservesContent()
    {
        var entry = new UserSessionEntry(Timestamp: 1700000000000, Content: "hi there");

        var line = SessionEntryCodec.Serialize(entry).TrimEnd('\n');
        var back = SessionEntryCodec.Deserialize(line);

        await Assert.That(back).IsTypeOf<UserSessionEntry>();
        await Assert.That(((UserSessionEntry)back).Content).IsEqualTo("hi there");
        await Assert.That(((UserSessionEntry)back).Timestamp).IsEqualTo(1700000000000);
    }

    [Test]
    public async Task RoundTrip_AssistantEntry_WithTextAndToolCalls_PreservesStructure()
    {
        var entry = new AssistantSessionEntry(
            Timestamp: 1700000000001,
            Content:
            [
                new TextBlock("thinking aloud"),
                new ToolCall("c1", "bash")
                {
                    Arguments = System.Text.Json.Nodes.JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
                },
            ],
            StopReason: StopReasons.ToolUse,
            Usage: new Usage { Input = 10, Output = 5, TotalTokens = 15 });

        var line = SessionEntryCodec.Serialize(entry).TrimEnd('\n');
        var back = SessionEntryCodec.Deserialize(line);

        await Assert.That(back).IsTypeOf<AssistantSessionEntry>();
        var assistant = (AssistantSessionEntry)back;
        await Assert.That(assistant.Content.Count).IsEqualTo(2);
        await Assert.That(assistant.Content[0]).IsTypeOf<TextBlock>();
        await Assert.That(((TextBlock)assistant.Content[0]).Text).IsEqualTo("thinking aloud");
        await Assert.That(assistant.Content[1]).IsTypeOf<ToolCall>();
        await Assert.That(((ToolCall)assistant.Content[1]).Name).IsEqualTo("bash");
        await Assert.That(assistant.StopReason).IsEqualTo(StopReasons.ToolUse);
        await Assert.That(assistant.Usage.Input).IsEqualTo(10);
        await Assert.That(assistant.Usage.Output).IsEqualTo(5);
        await Assert.That(assistant.Usage.TotalTokens).IsEqualTo(15);
    }

    [Test]
    public async Task RoundTrip_ToolResultEntry_PreservesErrorFlag()
    {
        var entry = new ToolResultSessionEntry(
            Timestamp: 1700000000002,
            ToolCallId: "c1",
            ToolName: "bash",
            Content: [new TextBlock("kaboom")],
            IsError: true);

        var line = SessionEntryCodec.Serialize(entry).TrimEnd('\n');
        var back = SessionEntryCodec.Deserialize(line);

        await Assert.That(back).IsTypeOf<ToolResultSessionEntry>();
        var tr = (ToolResultSessionEntry)back;
        await Assert.That(tr.ToolCallId).IsEqualTo("c1");
        await Assert.That(tr.ToolName).IsEqualTo("bash");
        await Assert.That(tr.IsError).IsTrue();
        await Assert.That(((TextBlock)tr.Content[0]).Text).IsEqualTo("kaboom");
    }

    [Test]
    public async Task Serialize_AppendsNewline()
    {
        var line = SessionEntryCodec.Serialize(new UserSessionEntry(0, "x"));

        await Assert.That(line.EndsWith('\n')).IsTrue();
    }

    [Test]
    public async Task Serialize_UsesKindDiscriminator()
    {
        var line = SessionEntryCodec.Serialize(new UserSessionEntry(0, "x"));

        await Assert.That(line).Contains("\"kind\":\"user\"");
    }

    [Test]
    public async Task Deserialize_MissingKind_Throws()
    {
        var bogus = """{"timestamp":0,"content":"x"}""";

        await Assert.That(() => SessionEntryCodec.Deserialize(bogus))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Deserialize_UnknownKind_Throws()
    {
        var bogus = """{"kind":"imaginary","timestamp":0}""";

        await Assert.That(() => SessionEntryCodec.Deserialize(bogus))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Deserialize_EmptyLine_Throws()
    {
        await Assert.That(() => SessionEntryCodec.Deserialize(""))
            .Throws<InvalidDataException>();
    }
}
