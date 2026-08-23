using System.Diagnostics.CodeAnalysis;

using Phi.Extensions;

namespace Phi.Extensions.Tests;

[UnconditionalSuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
public class NotifyLevelTests
{
    [Test]
    public async Task Has_Three_Levels()
    {
        var names = Enum.GetNames<NotifyLevel>().OrderBy(n => n).ToList();
        await Assert.That(names).IsEquivalentTo(["Error", "Info", "Warning"]);
    }

    [Test]
    public async Task Default_Is_Info()
    {
        await Assert.That(NotifyLevel.Info).IsEqualTo(default);
    }
}

public class MessageDeliveryTests
{
    [Test]
    public async Task Has_Two_Deliveries()
    {
        var names = Enum.GetNames<MessageDelivery>().OrderBy(n => n).ToList();
        await Assert.That(names).IsEquivalentTo(["FollowUp", "Steer"]);
    }
}

/// <summary>
/// Capability flag tests: bitwise combinations, undefined bits ignored.
/// </summary>
[UnconditionalSuppressMessage("Usage", "TUnitAssertions0005:Assert.That(...) should not be used with a constant value")]
public class CapabilityFlagTests
{
    [Test]
    public async Task Single_Flags_Set_One_Bit_Each()
    {
        var single = new[]
        {
            ExtensionCapability.Network,
            ExtensionCapability.FileSystemRead,
            ExtensionCapability.FileSystemWrite,
            ExtensionCapability.ProcessSpawn,
            ExtensionCapability.SecretsRead,
            ExtensionCapability.EnvironmentRead,
            ExtensionCapability.ClipboardRead,
            ExtensionCapability.ClipboardWrite,
            ExtensionCapability.UiInteract,
            ExtensionCapability.TranscriptWrite,
        };
        foreach (var c in single)
            await Assert.That(IsSingleBit(c)).IsTrue();
    }

    [Test]
    public async Task None_Is_Zero()
    {
        await Assert.That((int)ExtensionCapability.None).IsEqualTo(0);
    }

    [Test]
    public async Task HasFlag_Works_For_Combined()
    {
        var combined = ExtensionCapability.Network | ExtensionCapability.FileSystemRead;
        await Assert.That(combined.HasFlag(ExtensionCapability.Network)).IsTrue();
        await Assert.That(combined.HasFlag(ExtensionCapability.FileSystemRead)).IsTrue();
        await Assert.That(combined.HasFlag(ExtensionCapability.ProcessSpawn)).IsFalse();
    }

    [Test]
    public async Task Undefined_Bits_Do_Not_Throw()
    {
        // Future bits might be added; combining existing flags with an
        // "unknown" bit value must not throw. (Bitwise OR with a value
        // outside the enum is well-defined in C# — just sets extra bits.)
        var withUnknown = ExtensionCapability.Network | (ExtensionCapability)(1 << 30);
        await Assert.That(withUnknown.HasFlag(ExtensionCapability.Network)).IsTrue();
    }

    private static bool IsSingleBit(ExtensionCapability c)
    {
        var v = (int)c;
        return v != 0 && (v & (v - 1)) == 0;
    }
}
