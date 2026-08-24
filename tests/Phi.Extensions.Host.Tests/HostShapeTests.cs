using System.Reflection;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Locks down the public surface of the host package. Sprint 1 keeps the
/// surface minimal; adding methods to <see cref="ExtensionRuntime"/> or
/// <see cref="PhiApi"/> is intentional, and this test is the gate.
/// </summary>
[NotInParallel("host-shape")]
public class HostShapeTests
{
    [Test]
    public async Task ExtensionRuntime_Public_Methods_Are_Stable()
    {
        var expected = new[]
        {
            "AddPromptGuideline",
            "DiscoverAndLoad",
            "DiscoverAndTrustProjectExtensionsAsync",
            "Dispose",
            "Initialize",
            "InvalidateAllGenerations",
            "RegisterCommand",
            "RegisterCompiledExtension",
            "RegisterTool",
            "SubscribeEvent",
        };
        var actual = typeof(ExtensionRuntime)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .Distinct()
            .ToList();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    private static readonly string[] sourceArray =
        [
            "CapabilityEnforcement",
            "Commands",
            "Extensions",
            "LoadResults",
            "SetupResults",
            "Session",
            "UiBridge",
        ];

    [Test]
    public async Task ExtensionRuntime_Public_Properties_Are_Stable()
    {
        var actual = typeof(ExtensionRuntime)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        await Assert.That(actual).IsEquivalentTo(sourceArray.OrderBy(n => n).ToList());
    }

    [Test]
    public async Task ExtensionLoader_Is_Static()
    {
        var type = typeof(ExtensionLoader);
        await Assert.That(type.IsAbstract).IsTrue();
        await Assert.That(type.IsSealed).IsTrue();
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        await Assert.That(ctors).IsEmpty();
    }

    [Test]
    public async Task ExtensionLoader_Exposes_Load()
    {
        var methods = typeof(ExtensionLoader)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();
        await Assert.That(methods).IsEquivalentTo(["Load"]);
    }
}
