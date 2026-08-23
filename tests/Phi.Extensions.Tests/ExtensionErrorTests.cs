using Phi.Extensions;

namespace Phi.Extensions.Tests;

public class ExtensionErrorTests
{
    [Test]
    public async Task Inherits_From_Exception()
    {
        await Assert.That(typeof(ExtensionError).IsSubclassOf(typeof(Exception))).IsTrue();
    }

    [Test]
    public async Task Constructor_Sets_Message()
    {
        var ex = new ExtensionError("oops");
        await Assert.That(ex.Message).IsEqualTo("oops");
    }

    [Test]
    public async Task Constructor_Wraps_Inner_Exception()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new ExtensionError("wrapped", inner);
        await Assert.That(ex.InnerException).IsSameReferenceAs(inner);
    }
}
