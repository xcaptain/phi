using System.Reflection;
using Phi.Extensions;

namespace Phi.Extensions.Tests;

/// <summary>
/// Locks the public surface of <see cref="IPhiApi"/>. Adding a method,
/// removing one, or changing a signature will break this test — which is
/// the point. Sprint 0 freezes the surface; v0.x allows intentional breaks
/// via PR + Justification.
/// </summary>
[NotInParallel]
public class ApiShapeTests
{
    [Test]
    public async Task IPhiApi_Methods_Match_Frozen_List()
    {
        // The exact set of public methods on IPhiApi. Order matters for
        // stable diffs; keep alphabetical within each "section".
        // `IsSpecialName` filters out `get_*` / `set_*` property accessors
        // that GetMethods returns.
        var expected = new[]
        {
            "AddPromptGuideline",
            "AppendEntryAsync",
            "Notify",
            "On",                 // appears twice (Action / Func overloads)
            "On",
            "RegisterCommand",
            "RegisterMessageRenderer",
            "RegisterTool",
            "RegisterToolCard",
            "RegisterTranscriptLineRenderer",
            "SubmitCustomMessage",
            "SubmitTranscriptLine",
            "SubmitUserMessage",
            "SwitchModel",
            "SwitchProvider",
        };

        var actual = typeof(IPhiApi)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        await Assert.That(actual).IsEquivalentTo(expected.OrderBy(n => n).ToList());
    }

    [Test]
    public async Task IPhiApi_Properties_Are_Name_Version_Context()
    {
        var props = typeof(IPhiApi)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        await Assert.That(props).IsEquivalentTo(new[] { "Context", "Name", "Version" });
    }
}
