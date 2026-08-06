using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="PromptInput.BuildSessionPicker"/> index mapping. <c>OptionList</c>'s
/// <c>ItemActivated</c> index is the raw item position (date-group headers
/// included), so the record lookup must be position-parallel. Regression: a
/// record-counting loop silently missed sessions once the list spanned
/// multiple day groups.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class PromptInputSessionPickerTests
{
    private static SessionRecord Record(string id, long updatedAt, string model = "m") => new(
        id, "/cwd", model, $"session-{id}", updatedAt, updatedAt);

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Test]
    public async Task BuildSessionPicker_SingleGroup_EverySelectableIndexMapsToItsRecord()
    {
        var sessions = new[]
        {
            Record("a", NowMs()),
            Record("b", NowMs() - 100),
            Record("c", NowMs() - 200),
        };

        var (list, recordsByIndex) = PromptInput.BuildSessionPicker(sessions);

        // header + 3 sessions
        await Assert.That(list.Items.Count).IsEqualTo(4);
        await Assert.That(recordsByIndex.Count).IsEqualTo(list.Items.Count);

        // header maps to null
        await Assert.That(recordsByIndex[0]).IsNull();
        // sessions map position-parallel
        await Assert.That(recordsByIndex[1]!.Id).IsEqualTo("a");
        await Assert.That(recordsByIndex[2]!.Id).IsEqualTo("b");
        await Assert.That(recordsByIndex[3]!.Id).IsEqualTo("c");
    }

    [Test]
    public async Task BuildSessionPicker_MultiGroup_LastGroupSelectionMapsCorrectly()
    {
        // Two date groups: "Today" (header + a, b) then "Yesterday" (header + c).
        // This is the regression: the last group's session used to map past the
        // end (target null → nothing happened on select).
        var yesterday = NowMs() - (long)TimeSpan.FromDays(1).TotalMilliseconds;
        var sessions = new[]
        {
            Record("a", NowMs()),
            Record("b", NowMs() - 100),
            Record("c", yesterday),
        };

        var (list, recordsByIndex) = PromptInput.BuildSessionPicker(sessions);

        // Today header, a, b, Yesterday header, c
        await Assert.That(list.Items.Count).IsEqualTo(5);
        await Assert.That(recordsByIndex.Count).IsEqualTo(5);
        await Assert.That(recordsByIndex[0]).IsNull();
        await Assert.That(recordsByIndex[1]!.Id).IsEqualTo("a");
        await Assert.That(recordsByIndex[2]!.Id).IsEqualTo("b");
        await Assert.That(recordsByIndex[3]).IsNull();
        await Assert.That(recordsByIndex[4]!.Id).IsEqualTo("c");
    }

    [Test]
    public async Task BuildSessionPicker_MultiGroup_SelectablePositionsAlwaysMapToARecord()
    {
        var yesterday = NowMs() - (long)TimeSpan.FromDays(1).TotalMilliseconds;
        var sessions = new[]
        {
            Record("a", NowMs()),
            Record("c", yesterday),
        };

        var (list, recordsByIndex) = PromptInput.BuildSessionPicker(sessions);

        for (var i = 0; i < list.Items.Count; i++)
        {
            var enabled = ((OptionListItem)list.Items[i]).IsEnabled;
            if (enabled)
                await Assert.That(recordsByIndex[i]).IsNotNull();
            else
                await Assert.That(recordsByIndex[i]).IsNull();
        }
    }
}
