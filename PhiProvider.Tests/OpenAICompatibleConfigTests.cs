using PhiAgent;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiProvider.Tests;

public class OpenAICompatibleConfigTests
{
    [Test]
    public async Task DefaultsAreSensibleForOpenAi()
    {
        var config = new OpenAICompatibleConfig { ApiKey = "test" };

        await Assert.That(config.BaseUrl).IsEqualTo("https://api.openai.com/v1");
        await Assert.That(config.Api).IsEqualTo("openai-completions");
        await Assert.That(config.Provider).IsEqualTo("openai-compatible");
        await Assert.That(config.Timeout).IsEqualTo(TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task DeepSeekUrlOverridesBaseUrl()
    {
        var config = new OpenAICompatibleConfig
        {
            ApiKey = "test",
            BaseUrl = "https://api.deepseek.com",
            Provider = "deepseek",
        };

        await Assert.That(config.BaseUrl).IsEqualTo("https://api.deepseek.com");
        await Assert.That(config.Provider).IsEqualTo("deepseek");
    }
}

public class AgentToolTests
{
    [Test]
    public async Task RoundTripJson_PreservesNameAndParameters()
    {
        var tool = new Tool(
            Name: "bash",
            Description: "Run a shell command",
            Parameters: new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "object",
                ["properties"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["command"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "string",
                    },
                },
            });

        await Assert.That(tool.Name).IsEqualTo("bash");
        await Assert.That(tool.Parameters["type"]!.GetValue<string>()).IsEqualTo("object");
        await Assert.That(tool.Parameters["properties"]!["command"]!["type"]!.GetValue<string>()).IsEqualTo("string");
    }
}

public class ProviderEventTests
{
    [Test]
    public async Task TextDelta_KindIsTextDelta()
    {
        var ev = new ProviderTextDeltaEvent("hello");

        await Assert.That(ev.Kind).IsEqualTo("textDelta");
        await Assert.That(ev.Delta).IsEqualTo("hello");
    }

    [Test]
    public async Task ErrorEvent_DefaultDataIsNull()
    {
        var ev = new ProviderErrorEvent("boom");

        await Assert.That(ev.Kind).IsEqualTo("error");
        await Assert.That(ev.Data).IsNull();
    }
}