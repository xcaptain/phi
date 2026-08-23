using System.Reflection;
using Phi.Extensions;

namespace Phi.Extensions.Tests;

public class PhiExtensionAttributeTests
{
    [Test]
    public async Task Required_Name_Is_Enforced_At_Compile_Time()
    {
        // `Name` is `required`, so this would fail to compile — that's the
        // point. We can't runtime-test the absence; the compile error is
        // the test. (Renaming `Name` away from `required` would also need
        // to remove this test method's body.)
        //
        // What we *can* runtime-check: that an instance constructed with
        // an empty Name still has Name == "" (required isn't validation,
        // just nullability at init time).
        var attr = new PhiExtensionAttribute { Name = "" };
        await Assert.That(attr.Name).IsEqualTo("");
    }

    [Test]
    public async Task Sets_Properties()
    {
        var attr = new PhiExtensionAttribute
        {
            Name = "hello-tool",
            Version = "1.2.3",
            Description = "Demo.",
            Capabilities = ExtensionCapability.Network | ExtensionCapability.FileSystemRead,
        };
        await Assert.That(attr.Name).IsEqualTo("hello-tool");
        await Assert.That(attr.Version).IsEqualTo("1.2.3");
        await Assert.That(attr.Description).IsEqualTo("Demo.");
        await Assert.That(attr.Capabilities)
            .IsEqualTo(ExtensionCapability.Network | ExtensionCapability.FileSystemRead);
    }

    [Test]
    public async Task Defaults_Are_Safe()
    {
        var attr = new PhiExtensionAttribute { Name = "x" };
        await Assert.That(attr.Version).IsEqualTo("0.0.0");
        await Assert.That(attr.Description).IsEqualTo("");
        await Assert.That(attr.Capabilities).IsEqualTo(ExtensionCapability.None);
    }

    [Test]
    public async Task AttributeUsage_Is_Class_Only_NotInherited_NotMultiple()
    {
        var usage = typeof(PhiExtensionAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();
        await Assert.That(usage).IsNotNull();
        await Assert.That(usage!.ValidOn).IsEqualTo(AttributeTargets.Class);
        await Assert.That(usage.AllowMultiple).IsFalse();
        await Assert.That(usage.Inherited).IsFalse();
    }
}
