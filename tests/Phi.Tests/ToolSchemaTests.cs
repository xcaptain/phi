using Phi.Tools;

namespace Phi.Tests;

/// <summary>
/// Locks the source-generated tool schemas (Phi.SchemaGen) to the exact JSON
/// shape the reflection-based TypedSchema used to produce: camelCase property
/// names, <c>type</c> per property, <c>required</c> members, and
/// <c>[Description]</c> values. Guards the generator output against regressions.
/// </summary>
public class ToolSchemaTests
{
    [Test]
    public async Task ReadTool_Schema_MatchesExpectedShape()
    {
        var tool = new ReadTool();
        var schema = tool.Parameters;

        await Assert.That(schema["type"]!.GetValue<string>()).IsEqualTo("object");
        var props = schema["properties"]!.AsObject();
        await Assert.That(props.Count).IsEqualTo(3);
        await Assert.That(props["path"]!["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(props["path"]!["description"]!.GetValue<string>())
            .IsEqualTo("Path to the file to read");
        await Assert.That(props["offset"]!["type"]!.GetValue<string>()).IsEqualTo("integer");
        await Assert.That(props["limit"]!["type"]!.GetValue<string>()).IsEqualTo("integer");
        await Assert.That(schema["required"]!.AsArray().Select(n => n!.GetValue<string>()))
            .IsEquivalentTo(["path"]);
    }

    [Test]
    public async Task WriteTool_Schema_HasPathAndContentRequired()
    {
        var tool = new WriteTool();
        var schema = tool.Parameters;

        var props = schema["properties"]!.AsObject();
        await Assert.That(props["path"]!["description"]!.GetValue<string>())
            .IsEqualTo("File path to write to");
        await Assert.That(props["content"]!["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(schema["required"]!.AsArray().Select(n => n!.GetValue<string>()))
            .IsEquivalentTo(["path", "content"]);
    }

    [Test]
    public async Task EditTool_Schema_HasEditsArrayWithItemObject()
    {
        var tool = new EditTool();
        var schema = tool.Parameters;

        var props = schema["properties"]!.AsObject();
        var edits = props["edits"]!.AsObject();
        await Assert.That(edits["type"]!.GetValue<string>()).IsEqualTo("array");
        var item = edits["items"]!.AsObject();
        await Assert.That(item["type"]!.GetValue<string>()).IsEqualTo("object");
        var itemProps = item["properties"]!.AsObject();
        await Assert.That(itemProps["oldText"]!["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(itemProps["newText"]!["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(item["required"]!.AsArray().Select(n => n!.GetValue<string>()))
            .IsEquivalentTo(["oldText", "newText"]);
    }

    [Test]
    public async Task BashTool_Schema_OnlyCommandRequired()
    {
        var tool = new BashTool();
        var schema = tool.Parameters;

        var props = schema["properties"]!.AsObject();
        await Assert.That(props.Count).IsEqualTo(1);
        await Assert.That(props["command"]!["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(schema["required"]!.AsArray().Select(n => n!.GetValue<string>()))
            .IsEquivalentTo(["command"]);
    }
}
