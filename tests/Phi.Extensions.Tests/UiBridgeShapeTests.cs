using System.Reflection;
using Phi.Extensions;

namespace Phi.Extensions.Tests;

/// <summary>
/// Locks the public surface of <see cref="IPhiUiBridge"/>.
/// </summary>
[NotInParallel]
public class UiBridgeShapeTests
{
    [Test]
    public async Task IPhiUiBridge_Methods_Match_Frozen_List()
    {
        var expected = new[]
        {
            "ConfirmAsync",
            "FlashError",
            "InputAsync",
            "Notify",
            "NotifyStatus",
            "SelectAsync",
            "SubmitTranscriptLine",
        };

        var actual = typeof(IPhiUiBridge)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task IPhiUiBridge_Has_One_Property_HasUi()
    {
        var props = typeof(IPhiUiBridge)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToList();

        await Assert.That(props).IsEquivalentTo(new[] { "HasUi" });
    }
}
