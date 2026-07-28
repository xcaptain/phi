using PhiAgent;
using PhiCoding.Tools;

namespace PhiCoding;

public static class BuiltInTools
{
    public static IReadOnlyList<IHarnessTool> CreateDefault() =>
    [
        new BashTool(),
        new ReadTool(),
        new WriteTool(),
        new EditTool(),
    ];
}
