using System.Reflection;
using Phi.Extensions;

namespace Phi.Extensions.Tests;

/// <summary>
/// Locks the public surface of <see cref="IPhiContext"/> — adding /
/// removing a property is a breaking change.
/// </summary>
[NotInParallel]
public class ContextShapeTests
{
    [Test]
    public async Task IPhiContext_Properties_Match_Frozen_List()
    {
        var expected = new[]
        {
            "Cwd",
            "HasUi",
            "IsRunning",
            "Model",
            "ProviderName",
            "SessionId",
            "SystemPrompt",
            "Transcript",
            "Ui",
        };

        var actual = typeof(IPhiContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task IPhiContext_No_Methods()
    {
        // IPhiContext is read-only — no methods allowed.
        var methods = typeof(IPhiContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToList();

        await Assert.That(methods).IsEmpty();
    }
}
