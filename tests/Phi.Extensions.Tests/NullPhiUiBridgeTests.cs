using Phi.Extensions;

namespace Phi.Extensions.Tests;

public class NullPhiUiBridgeTests
{
    [Test]
    public async Task HasUi_Is_False()
    {
        var b = new NullPhiUiBridge();
        await Assert.That(b.HasUi).IsFalse();
    }

    [Test]
    public async Task SelectAsync_Returns_Null()
    {
        var b = new NullPhiUiBridge();
        var result = await b.SelectAsync("title", ["a", "b"]);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ConfirmAsync_Returns_False()
    {
        var b = new NullPhiUiBridge();
        var result = await b.ConfirmAsync("title", "msg");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task InputAsync_Returns_Null()
    {
        var b = new NullPhiUiBridge();
        var result = await b.InputAsync("title");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Calls_Do_Not_Throw()
    {
        // Notify / NotifyStatus / FlashError / SubmitTranscriptLine are
        // fire-and-forget; the no-op bridge must silently discard.
        var b = new NullPhiUiBridge();
        b.Notify("hi");
        b.NotifyStatus("status");
        b.FlashError("err", persistent: true);
        b.SubmitTranscriptLine(new TranscriptLine("t", "i", "c"));
        await Task.CompletedTask;   // suppress async-warning
    }
}
