using Phi.Tui.Components.ToolCards;

namespace Phi.Tests.Tui;

public class SkillInvocationCardTests
{
    [Test]
    public async Task Preview_ShortContent_ReturnsAll_NoTruncation()
    {
        var content = "line 1\nline 2\nline 3";

        var preview = SkillInvocationCard.Preview(content, 5, out var hasMore);

        await Assert.That(preview).IsEqualTo(content);
        await Assert.That(hasMore).IsFalse();
    }

    [Test]
    public async Task Preview_ExactlyFiveLines_NoTruncation()
    {
        var content = string.Join('\n', Enumerable.Range(1, 5).Select(i => $"line {i}"));

        var preview = SkillInvocationCard.Preview(content, 5, out var hasMore);

        await Assert.That(preview).IsEqualTo(content);
        await Assert.That(hasMore).IsFalse();
    }

    [Test]
    public async Task Preview_MoreThanFiveLines_TruncatesToFive_AndReportsHasMore()
    {
        var content = string.Join('\n', Enumerable.Range(1, 8).Select(i => $"line {i}"));

        var preview = SkillInvocationCard.Preview(content, 5, out var hasMore);

        await Assert.That(preview)
            .IsEqualTo(string.Join('\n', Enumerable.Range(1, 5).Select(i => $"line {i}")));
        await Assert.That(hasMore).IsTrue();
    }

    [Test]
    public async Task Preview_NormalizesCrLf()
    {
        var preview = SkillInvocationCard.Preview("a\r\nb\r\nc\r\nd\r\ne\r\nf", 5, out var hasMore);

        await Assert.That(preview).IsEqualTo("a\nb\nc\nd\ne");
        await Assert.That(hasMore).IsTrue();
    }

    [Test]
    public async Task Preview_EmptyContent_NoTruncation()
    {
        var preview = SkillInvocationCard.Preview("", 5, out var hasMore);

        await Assert.That(preview).IsEqualTo("");
        await Assert.That(hasMore).IsFalse();
    }
}
