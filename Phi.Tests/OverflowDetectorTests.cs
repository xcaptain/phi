namespace Phi.Tests;

public class OverflowDetectorTests
{
    [Test]
    public async Task IsOverflow_AnthropicStyle_True()
    {
        await Assert.That(OverflowDetector.IsOverflow(
            "prompt is too long; please reduce the length of your input")).IsTrue();
        await Assert.That(OverflowDetector.IsOverflow(
            "Context length exceeded: 200000 tokens")).IsTrue();
    }

    [Test]
    public async Task IsOverflow_UnrelatedError_False()
    {
        await Assert.That(OverflowDetector.IsOverflow(null)).IsFalse();
        await Assert.That(OverflowDetector.IsOverflow("")).IsFalse();
        await Assert.That(OverflowDetector.IsOverflow("Connection refused")).IsFalse();
        await Assert.That(OverflowDetector.IsOverflow("Invalid API key")).IsFalse();
    }
}
