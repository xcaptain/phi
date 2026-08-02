using PhiCoding.Tui;

namespace PhiCoding.Tests;

public class ToolCardRegistryTests
{
    [Test]
    public async Task For_Read_ReturnsReadToolCard()
    {
        await Assert.That(ToolCardRegistry.For("read")).IsTypeOf<ReadToolCard>();
    }

    [Test]
    public async Task For_Write_ReturnsWriteToolCard()
    {
        await Assert.That(ToolCardRegistry.For("write")).IsTypeOf<WriteToolCard>();
    }

    [Test]
    public async Task For_Edit_ReturnsEditToolCard()
    {
        await Assert.That(ToolCardRegistry.For("edit")).IsTypeOf<EditToolCard>();
    }

    [Test]
    public async Task For_Bash_ReturnsBashToolCard()
    {
        await Assert.That(ToolCardRegistry.For("bash")).IsTypeOf<BashToolCard>();
    }

    [Test]
    public async Task For_UnknownName_ReturnsGenericToolCard()
    {
        await Assert.That(ToolCardRegistry.For("grep")).IsTypeOf<GenericToolCard>();
        await Assert.That(ToolCardRegistry.For("mcp__filesystem__list")).IsTypeOf<GenericToolCard>();
    }
}
