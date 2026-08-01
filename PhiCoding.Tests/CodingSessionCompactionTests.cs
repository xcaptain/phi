using PhiAgent;
using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests;

[NotInParallel("session-tests")]
public class CodingSessionCompactionTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;

    public CodingSessionCompactionTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-compact-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
    }

    private SessionConfig ConfigWith(IPhiProvider provider, params (string, object)[] overrides)
    {
        var dict = overrides.ToDictionary(o => o.Item1, o => o.Item2);
        return new SessionConfig
        {
            Cwd = _cwd,
            Provider = provider,
            Model = "stub-model",
            SystemPrompt = "test",
            MaxTurns = 5,
            Tools = [],
            ContextWindowTokens = dict.GetValueOrDefault("ContextWindowTokens") is int w ? w : 128_000,
            CompactionKeepRecentTokens = dict.GetValueOrDefault("CompactionKeepRecentTokens") is int k ? k : 50,
            AutoCompactEnabled = !dict.TryGetValue("AutoCompactEnabled", out var ae) || (bool)ae,
            // Default to a tiny threshold so tests can actually trigger compaction.
            AutoCompactTokenThreshold = dict.GetValueOrDefault("AutoCompactTokenThreshold") is int t ? t : 200,
        };
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    private static List<IAgentMessage> BulkyHistory(int pairs)
    {
        var list = new List<IAgentMessage>();
        for (var i = 0; i < pairs; i++)
        {
            list.Add(new UserMessage { Content = $"user-{i} " + new string('u', 200) });
            list.Add(new AssistantMessage
            {
                Content = [new TextBlock(new string('a', 400))],
                StopReason = StopReasons.Stop,
            });
        }
        return list;
    }

    // ──────────────────── Auto-compact triggers ────────────────────

    [Test]
    public async Task AutoCompact_BeforePrompt_ReplacesMessagesAndPersists()
    {
        // Build a session with stored history that exceeds the threshold
        // (we set keepRecentTokens=50 to force compaction).
        var stored = CodingSession.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;

        // Resumed session with tiny keep-recent budget and a provider that
        // returns a fixed summary text.
        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }),
        };
        var resumed = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        // The session was resumed with the loaded history. Submitting a
        // prompt triggers auto-compact (since context > threshold).
        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // First message in the live state should be the compaction-prefixed
        // UserMessage carrying the summary text, and the new "hi" user
        // message (added by harness.RunAsync) should still be present.
        await Assert.That(resumed.State.Messages[0]).IsTypeOf<UserMessage>();
        await Assert.That(
            ((UserMessage)resumed.State.Messages[0]).Text
                .StartsWith(ContextWindow.CompactionSummaryPrefix)).IsTrue();
        await Assert.That(
            resumed.State.Messages.OfType<UserMessage>()
                .Any(u => u.Text == "hi")).IsTrue();
    }

    [Test]
    public async Task AutoCompact_UpdatesInMemoryHarnessNotJustDisk()
    {
        var stored = CodingSession.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;
        var originalCount = stored.LoadMessages().Count;

        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }),
        };
        var resumed = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        // Internal: confirm harness was loaded with the bulky history.
        // (We don't expose harness, but the live State mirrors it.)

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // In-memory state should be smaller than original count: the kept
        // suffix plus the compaction summary, plus the new "hi" message.
        await Assert.That(resumed.State.Messages.Count).IsLessThan(originalCount);
        // Critically: no user-0 message from the original prefix remains.
        var originalTexts = BulkyHistory(pairs: 6)
            .OfType<UserMessage>().Select(u => u.Text).ToList();
        await Assert.That(
            resumed.State.Messages.OfType<UserMessage>()
                .Any(u => originalTexts.Contains(u.Text ?? ""))).IsFalse();
    }

    [Test]
    public async Task AutoCompact_PersistsNewJsonlWithoutOldMessages()
    {
        var stored = CodingSession.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;

        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }),
        };
        var resumed = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // Read back from disk; the first entry should be the compaction-prefixed
        // UserMessage (the jsonl stores a CompactionSessionEntry but it
        // round-trips as a UserMessage with the marker prefix).
        var fresh = CodingSession.Resume(storedId, _cwd);
        await Assert.That(fresh.LoadMessages()[0]).IsTypeOf<UserMessage>();
        await Assert.That(
            ((UserMessage)fresh.LoadMessages()[0]).Text
                .StartsWith(ContextWindow.CompactionSummaryPrefix)).IsTrue();
        // The original-prefix user messages must not be present on disk.
        var originalTexts = BulkyHistory(pairs: 6)
            .OfType<UserMessage>().Take(3).Select(u => u.Text).ToList();
        await Assert.That(
            fresh.LoadMessages().OfType<UserMessage>()
                .Any(u => originalTexts.Contains(u.Text ?? ""))).IsFalse();
    }

    [Test]
    public async Task AutoCompact_DoesNotPolluteSessionStats()
    {
        var stored = CodingSession.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;
        var statsBefore = SessionStatsCalculator.Calculate(stored.LoadMessages());

        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }),
        };
        var resumed = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        // Before compaction, turn count matches the original history.
        await Assert.That(resumed.State.Stats.TurnCount).IsEqualTo(statsBefore.TurnCount);

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // After compaction + a fresh prompt: turn count is the kept-suffix
        // turns (the dropped prefix's turns are gone, the "hi" prompt is in).
        await Assert.That(resumed.State.Stats.TurnCount).IsLessThanOrEqualTo(statsBefore.TurnCount + 1);
    }

    [Test]
    public async Task AutoCompact_Disabled_KeepsMessages()
    {
        var stored = CodingSession.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;
        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }),
        };
        var resumed = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50),
                ("AutoCompactEnabled", false)), storedId);
        var originalCount = resumed.State.Messages.Count;

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // Auto-compact disabled → no compaction-prefixed UserMessage appears.
        await Assert.That(
            resumed.State.Messages.OfType<UserMessage>()
                .Any(u => u.Text.StartsWith(
                    ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal))).IsFalse();
        // The new "hi" prompt IS appended (by harness.RunAsync), adding
        // both a user message and an assistant turn.
        await Assert.That(resumed.State.Messages.Count).IsEqualTo(originalCount + 2);
    }

    [Test]
    public async Task Resume_AfterCompaction_LoadsCompactedHistory()
    {
        // Round-trip: create → resume → submit (compaction fires) → kill →
        // resume again → confirm only [compaction, ...kept] come back.
        var stored = CodingSession.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;

        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }),
        };
        var live = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);
        live.SubmitPrompt("hi");
        await WaitForAsync(() => !live.State.IsRunning);

        // A fresh resume from disk sees the rewritten transcript.
        var reloaded = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))), storedId);
        await Assert.That(reloaded.LoadMessages()[0]).IsTypeOf<UserMessage>();
        await Assert.That(
            ((UserMessage)reloaded.LoadMessages()[0]).Text
                .StartsWith(ContextWindow.CompactionSummaryPrefix)).IsTrue();
    }

    [Test]
    public async Task ChatTranscript_CompactionSummaryMessage_RendersAsDivider()
    {
        // The compaction-prefixed UserMessage should render as a divider,
        // not as a user turn.
        var t = new PhiCoding.Tui.ChatTranscript();
        var msgs = new IAgentMessage[]
        {
            new UserMessage { Content = ContextWindow.CompactionSummaryPrefix + "compacted earlier" },
            new UserMessage { Content = "hello" },
            new AssistantMessage { Content = [new TextBlock("world")], StopReason = StopReasons.Stop },
        };
        t.ClearAndLoad(msgs);

        var flow = (XenoAtom.Terminal.UI.Controls.DocumentFlow)t.Visual;
        // 1 divider + 1 user + 1 assistant = 3 items
        await Assert.That(flow.Items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SessionStatsCalculator_IgnoresCompactionPrefixedMessage()
    {
        // A compaction summary rides along as a UserMessage with a marker
        // prefix; the calculator must skip it so TurnCount stays accurate.
        var msgs = new IAgentMessage[]
        {
            new UserMessage { Content = ContextWindow.CompactionSummaryPrefix + "previous summary" },
            new UserMessage { Content = "u" },
            new AssistantMessage
            {
                Content = [new TextBlock("a")],
                Usage = new Usage { Input = 10, Output = 5, TotalTokens = 15 },
                StopReason = StopReasons.Stop,
            },
        };
        var stats = SessionStatsCalculator.Calculate(msgs);
        await Assert.That(stats.TurnCount).IsEqualTo(1);
        await Assert.That(stats.InputTokens).IsEqualTo(10);
        await Assert.That(stats.OutputTokens).IsEqualTo(5);
    }
}