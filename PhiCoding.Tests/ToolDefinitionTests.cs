using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class ToolDefinitionTests
{
    [Test]
    public async Task ToTool_ProducesSlimToolWithSameNameAndDescription()
    {
        var definition = new ToolDefinition(
            Name: "my_tool",
            Description: "Does something useful",
            Parameters: new JsonObject { ["type"] = "object" });

        var tool = definition.ToTool();

        await Assert.That(tool.Name).IsEqualTo("my_tool");
        await Assert.That(tool.Description).IsEqualTo("Does something useful");
        await Assert.That(tool.Parameters["type"]!.GetValue<string>()).IsEqualTo("object");
    }

    [Test]
    public async Task ToTool_DropsPromptMetadata()
    {
        var definition = new ToolDefinition(
            Name: "my_tool",
            Description: "test",
            Parameters: new JsonObject { ["type"] = "object" },
            PromptSnippet: "Some hint",
            PromptGuidelines: ["First guideline", "Second guideline"]);

        var tool = definition.ToTool();

        // Tool doesn't carry prompt metadata — that's application-level concern
        await Assert.That(tool.GetType().GetProperty("PromptSnippet")).IsNull();
        await Assert.That(tool.GetType().GetProperty("PromptGuidelines")).IsNull();
    }

    [Test]
    public async Task ToolDefinition_ParametersAcceptNestedJsonObject()
    {
        var definition = new ToolDefinition(
            Name: "bash",
            Description: "Run a command",
            Parameters: new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["command"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Shell command to run",
                    },
                },
                ["required"] = new JsonArray { "command" },
            });

        var schema = definition.Parameters;
        await Assert.That(schema["type"]!.GetValue<string>()).IsEqualTo("object");
        await Assert.That(schema["properties"]!["command"]!["type"]!.GetValue<string>())
            .IsEqualTo("string");
        await Assert.That(schema["required"]![0]!.GetValue<string>()).IsEqualTo("command");
    }

    [Test]
    public async Task BashTool_DefinitionHasExpectedSchema()
    {
        var tool = new BashTool();

        await Assert.That(tool.Definition.Name).IsEqualTo("bash");
        await Assert.That(tool.Definition.Parameters["required"]![0]!.GetValue<string>())
            .IsEqualTo("command");
    }
}