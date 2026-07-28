namespace PhiCoding.Tui;

/// <summary>
/// Converts <see cref="ChatItem"/>s into a flat list of wrapped transcript
/// lines for the transcript view. Pure function — unit-tested directly.
/// Hard-wraps at <paramref name="width"/> columns; inserts one blank line
/// between items; user messages get a "&gt; " prompt prefix.
/// </summary>
internal static class TranscriptWrapper
{
    public static List<TranscriptLine> Wrap(IReadOnlyList<ChatItem> items, int width)
    {
        var result = new List<TranscriptLine>();
        foreach (var item in items)
        {
            if (result.Count > 0)
                result.Add(new TranscriptLine("", TranscriptStyle.Default));

            foreach (var line in RenderLines(item))
                WrapLine(line, width, result);
        }
        return result;
    }

    internal static IEnumerable<TranscriptLine> RenderLines(ChatItem item)
    {
        if (item.StyledLines is { } styled)
            return styled;

        var lines = SplitLines(item.Text.ToString());
        if (item.Kind == ChatItemKind.User)
        {
            return lines.Select((t, i) => new TranscriptLine(
                (i == 0 ? "> " : "  ") + t, item.DefaultStyle));
        }
        return lines.Select(t => new TranscriptLine(t, item.DefaultStyle));
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Length == 0 ? [""] : text.Replace("\r\n", "\n").Split('\n');

    private static void WrapLine(TranscriptLine line, int width, List<TranscriptLine> output)
    {
        var text = line.Text;
        if (text.Length == 0)
        {
            output.Add(line);
            return;
        }
        for (var i = 0; i < text.Length; i += width)
            output.Add(new TranscriptLine(
                text.Substring(i, Math.Min(width, text.Length - i)), line.Style));
    }
}
