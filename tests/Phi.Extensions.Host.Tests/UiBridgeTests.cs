using Phi.Extensions;
using Phi.Extensions.Host;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// <see cref="PhiUiBridge"/> is a pure forwarder to <see cref="IUiSink"/>:
/// every <see cref="IPhiUiBridge"/> call lands on the sink with identical
/// arguments. These tests pin that contract so the bridge stays a thin
/// adapter and extensions can rely on <c>HasUi</c> / dialog return values
/// coming straight from the UI layer.
/// </summary>
[NotInParallel("ui-bridge")]
public class UiBridgeTests
{
    /// <summary>Recording fake sink for behavior assertions.</summary>
    private sealed class FakeSink : IUiSink
    {
        public bool HasUi { get; set; }
        public List<(string Message, NotifyLevel Level)> Notifies { get; } = [];
        public List<string> Statuses { get; } = [];
        public List<(string Message, bool Persistent)> Errors { get; } = [];
        public List<TranscriptLine> Lines { get; } = [];
        public Func<string, IReadOnlyList<string>, TimeSpan?, Task<string?>>? OnSelect { get; set; }
        public Func<string, string, TimeSpan?, Task<bool>>? OnConfirm { get; set; }
        public Func<string, string, TimeSpan?, Task<string?>>? OnInput { get; set; }

        public void Notify(string message, NotifyLevel level) => Notifies.Add((message, level));
        public void NotifyStatus(string message) => Statuses.Add(message);
        public void FlashError(string message, bool persistent) => Errors.Add((message, persistent));
        public void SubmitTranscriptLine(TranscriptLine line) => Lines.Add(line);

        public Task<string?> ShowSelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
            => OnSelect?.Invoke(title, options, timeout) ?? Task.FromResult<string?>(null);

        public Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout)
            => OnConfirm?.Invoke(title, message, timeout) ?? Task.FromResult(false);

        public Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout)
            => OnInput?.Invoke(title, placeholder, timeout) ?? Task.FromResult<string?>(null);
    }

    [Test]
    public async Task HasUi_Forwards_FromSink()
    {
        var sink = new FakeSink { HasUi = true };
        var bridge = new PhiUiBridge(sink);
        await Assert.That(bridge.HasUi).IsTrue();

        sink.HasUi = false;
        await Assert.That(bridge.HasUi).IsFalse();
    }

    [Test]
    public async Task Notify_Forwards_MessageAndLevel()
    {
        var sink = new FakeSink();
        var bridge = new PhiUiBridge(sink);

        bridge.Notify("hello", NotifyLevel.Warning);
        bridge.Notify("default-info");

        await Assert.That(sink.Notifies).IsEquivalentTo(new[]
        {
            (Message: "hello", Level: NotifyLevel.Warning),
            (Message: "default-info", Level: NotifyLevel.Info),
        });
    }

    [Test]
    public async Task NotifyStatus_And_FlashError_Forward()
    {
        var sink = new FakeSink();
        var bridge = new PhiUiBridge(sink);

        bridge.NotifyStatus("status-line");
        bridge.FlashError("oops", persistent: true);
        bridge.FlashError("transient", persistent: false);

        await Assert.That(sink.Statuses).IsEquivalentTo(["status-line"]);
        await Assert.That(sink.Errors).IsEquivalentTo(new[]
        {
            (Message: "oops", Persistent: true),
            (Message: "transient", Persistent: false),
        });
    }

    [Test]
    public async Task SubmitTranscriptLine_Forwards()
    {
        var sink = new FakeSink();
        var bridge = new PhiUiBridge(sink);

        var line = new TranscriptLine(
            Type: "my-ext:progress",
            Id: "abc",
            Content: "building…",
            Details: new Dictionary<string, object?> { ["percent"] = 42 });
        bridge.SubmitTranscriptLine(line);

        await Assert.That(sink.Lines).Count().IsEqualTo(1);
        await Assert.That(sink.Lines[0].Type).IsEqualTo("my-ext:progress");
        await Assert.That(sink.Lines[0].Id).IsEqualTo("abc");
        await Assert.That(sink.Lines[0].Content).IsEqualTo("building…");
        await Assert.That(sink.Lines[0].Details!["percent"]).IsEqualTo(42);
    }

    [Test]
    public async Task SelectAsync_Forwards_And_Returns_SinkResult()
    {
        var sink = new FakeSink
        {
            OnSelect = (title, options, timeout) =>
            {
                // Sink can inspect the request before responding.
                if (options.Count == 0) return Task.FromResult<string?>(null);
                return Task.FromResult<string?>(options[1]);
            },
        };
        var bridge = new PhiUiBridge(sink);

        var picked = await bridge.SelectAsync("Pick", ["a", "b", "c"], TimeSpan.FromSeconds(10));
        await Assert.That(picked).IsEqualTo("b");
    }

    [Test]
    public async Task ConfirmAsync_Forwards_And_Returns_SinkResult()
    {
        var sink = new FakeSink
        {
            OnConfirm = (title, message, _) =>
            {
                // Sink could render the dialog here; in tests we just decide.
                return Task.FromResult(message.Contains("rm -rf") ? false : true);
            },
        };
        var bridge = new PhiUiBridge(sink);

        await Assert.That(await bridge.ConfirmAsync("Allow?", "rm -rf /tmp", null)).IsFalse();
        await Assert.That(await bridge.ConfirmAsync("Allow?", "echo hello", null)).IsTrue();
    }

    [Test]
    public async Task InputAsync_Forwards_And_Returns_SinkResult()
    {
        var sink = new FakeSink
        {
            OnInput = (_, _, _) => Task.FromResult<string?>("typed value"),
        };
        var bridge = new PhiUiBridge(sink);

        var entered = await bridge.InputAsync("Title", "placeholder", null);
        await Assert.That(entered).IsEqualTo("typed value");
    }

    [Test]
    public async Task NullSink_HasUiFalse_AndDialogsReturnDefaults()
    {
        // The bridge used in headless contexts (CI / automation / tests)
        // must report HasUi=false and let dialogs short-circuit to no-op
        // defaults — extensions check HasUi to skip UI work entirely.
        var bridge = new PhiUiBridge(new NullUiSink());

        await Assert.That(bridge.HasUi).IsFalse();
        await Assert.That(await bridge.SelectAsync("t", ["a"], null)).IsNull();
        await Assert.That(await bridge.ConfirmAsync("t", "m", null)).IsFalse();
        await Assert.That(await bridge.InputAsync("t", "p", null)).IsNull();

        // No-throw sanity: notifications + line submission are silent.
        bridge.Notify("discarded");
        bridge.NotifyStatus("discarded");
        bridge.FlashError("discarded", persistent: false);
        bridge.SubmitTranscriptLine(new TranscriptLine("any:type", "id", "body"));
    }

    [Test]
    public void Ctor_NullSink_Throws()
    {
        IUiSink nullSink = null!;
        Assert.Throws<ArgumentNullException>(() => new PhiUiBridge(nullSink));
    }

    [Test]
    public async Task Ctor_Accessor_RebindsSink_Lazily()
    {
        // Sprint 3 lazy-binding: the TUI / Avalonia shell rebuilds the chat
        // page (and the sink that wraps its transcript + status bar) on
        // every session switch. The accessor overload lets the bridge
        // follow the swap without the extension knowing — extensions call
        // bridge.Notify / bridge.FlashError and the call lands on whatever
        // sink is current.
        var sinkA = new FakeSink { HasUi = true };
        var sinkB = new FakeSink { HasUi = false };
        IUiSink current = sinkA;
        var bridge = new PhiUiBridge(() => current);

        await Assert.That(bridge.HasUi).IsTrue();
        bridge.Notify("first");
        await Assert.That(sinkA.Notifies.Count).IsEqualTo(1);
        await Assert.That(sinkB.Notifies.Count).IsEqualTo(0);

        current = sinkB;
        await Assert.That(bridge.HasUi).IsFalse();
        bridge.Notify("second");
        await Assert.That(sinkA.Notifies.Count).IsEqualTo(1);
        await Assert.That(sinkB.Notifies.Count).IsEqualTo(1);
    }

    [Test]
    public void Ctor_Accessor_NullCurrent_Throws()
    {
        Func<IUiSink> nullAccessor = () => null!;
        var bridge = new PhiUiBridge(nullAccessor);
        Assert.Throws<InvalidOperationException>(() => bridge.Notify("x"));
    }
}
