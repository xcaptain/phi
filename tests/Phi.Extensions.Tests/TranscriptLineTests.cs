using Phi.Extensions;

namespace Phi.Extensions.Tests;

public class TranscriptLineTests
{
    [Test]
    public async Task Required_Fields_Can_Be_Set()
    {
        var line = new TranscriptLine(
            Type: "multi-agent:subagent-progress",
            Id: "subagent:abc",
            Content: "🤖 [explorer] starting: find Foo",
            Details: new Dictionary<string, object?>
            {
                ["role"] = "explorer",
                ["status"] = "running",
            });

        await Assert.That(line.Type).IsEqualTo("multi-agent:subagent-progress");
        await Assert.That(line.Id).IsEqualTo("subagent:abc");
        await Assert.That(line.Content).IsEqualTo("🤖 [explorer] starting: find Foo");
        await Assert.That(line.Details!["role"]).IsEqualTo("explorer");
    }

    [Test]
    public async Task Details_Defaults_To_Null()
    {
        var line = new TranscriptLine(Type: "t", Id: "i", Content: "c");
        await Assert.That(line.Details).IsNull();
    }
}
