using Phi.Agent;
using Phi.Prompts;
using Phi.Sessions;
using Phi.Tests.Helpers;

namespace Phi.Tests;

[NotInParallel(["session-tests", TuiTestGroups.BindingManager])]
public class SessionCompactionTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;
    private readonly string _previousPhiHome;
    private readonly Phi.Providers.ProviderManager _providerManager = new();
    private readonly SessionFactory _factory;

    public SessionCompactionTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-compact-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _phiHome;
        _factory = new SessionFactory(_providerManager);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SessionPaths.PhiHome = _previousPhiHome;
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
            SystemPrompt = new SystemPromptOptions { ResolvedSystemPrompt = "test" },
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
        var stored = Session.Create(_cwd, "m");
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
        var resumed = _factory.Resume(
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
                .StartsWith(ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(
            resumed.State.Messages.OfType<UserMessage>()
                .Any(u => u.Text == "hi")).IsTrue();
    }

    [Test]
    public async Task AutoCompact_UpdatesInMemoryHarnessNotJustDisk()
    {
        var stored = Session.Create(_cwd, "m");
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
        var resumed = _factory.Resume(
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
        var stored = Session.Create(_cwd, "m");
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
        var resumed = _factory.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // Read back from disk; the first entry should be the compaction-prefixed
        // UserMessage (the jsonl stores a CompactionSessionEntry but it
        // round-trips as a UserMessage with the marker prefix).
        var fresh = Session.Resume(storedId, _cwd);
        await Assert.That(fresh.LoadMessages()[0]).IsTypeOf<UserMessage>();
        await Assert.That(
            ((UserMessage)fresh.LoadMessages()[0]).Text
                .StartsWith(ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal)).IsTrue();
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
        var stored = Session.Create(_cwd, "m");
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
        var resumed = _factory.Resume(
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
        var stored = Session.Create(_cwd, "m");
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
        var resumed = _factory.Resume(
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
        var stored = Session.Create(_cwd, "m");
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
        var live = _factory.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);
        live.SubmitPrompt("hi");
        await WaitForAsync(() => !live.State.IsRunning);

        // A fresh resume from disk sees the rewritten transcript.
        var reloaded = _factory.Resume(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))), storedId);
        await Assert.That(reloaded.LoadMessages()[0]).IsTypeOf<UserMessage>();
        await Assert.That(
            ((UserMessage)reloaded.LoadMessages()[0]).Text
                .StartsWith(ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ChatTranscript_CompactionSummaryMessage_RendersAsDivider()
    {
        // The compaction-prefixed UserMessage should render as a divider,
        // not as a user turn.
        var t = new Phi.Tui.Components.ChatTranscript();
        t.Bind(new MockSession());
        var msgs = new IAgentMessage[]
        {
            new UserMessage { Content = ContextWindow.CompactionSummaryPrefix + "compacted earlier" },
            new UserMessage { Content = "hello" },
            new AssistantMessage { Content = [new TextBlock("world")], StopReason = StopReasons.Stop },
        };
        t.ClearAndLoad(msgs);

        var flow = t.Flow;
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

    [Test]
    public async Task AutoCompact_PersistsCumulativeFileOpsInCompactionEntry()
    {
        // History with file-touching tool calls: read/edit on concrete paths.
        // After auto-compaction, the on-disk CompactionSessionEntry.Details
        // should carry those paths so the next compaction can surface them
        // in its <read-files>/<modified-files> prompt sections.
        var stored = Session.Create(_cwd, "m");
        var history = new List<IAgentMessage>
        {
            new UserMessage { Content = "u0" },
            new AssistantMessage
            {
                Content =
                [
                    new TextBlock("checking files"),
                    new ToolCall("t1", "read") { Arguments = new() { ["path"] = "src/a.ts" } },
                    new ToolCall("t2", "edit") { Arguments = new() { ["path"] = "src/b.ts" } },
                ],
                StopReason = StopReasons.ToolUse,
            },
            new ToolResultMessage
            {
                ToolCallId = "t1", ToolName = "read",
                Content = [new TextBlock(new string('x', 400))],
            },
            new ToolResultMessage
            {
                ToolCallId = "t2", ToolName = "edit",
                Content = [new TextBlock(new string('y', 400))],
            },
            new AssistantMessage { Content = [new TextBlock("done")], StopReason = StopReasons.Stop },
            new UserMessage { Content = "more work please" },
            new AssistantMessage { Content = [new TextBlock(new string('z', 400))], StopReason = StopReasons.Stop },
        };
        foreach (var m in history)
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
            else if (m is ToolResultMessage tr) stored.AppendMessage(tr);
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
        var resumed = _factory.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        var entries = resumed.Storage.ReadAll().ToList();
        var compaction = entries.OfType<CompactionSessionEntry>().Single();
        await Assert.That(compaction.Details).IsNotNull();
        await Assert.That(compaction.Details!.ReadFiles).Contains("src/a.ts");
        await Assert.That(compaction.Details.ModifiedFiles).Contains("src/b.ts");
    }

    [Test]
    public async Task Resume_AfterCompaction_RestoresCumulativeFileOps()
    {
        // First compaction writes Details. The session record stays
        // indexed across the in-place rewrite, so a second compaction
        // resumes the same id, restores Details1 from the on-disk entry,
        // and unions in the new round's tool-call paths. After the second
        // compaction the file only holds the second entry (the first is
        // overwritten by the rewrite), and its Details must carry both
        // files — proving the carry-forward worked end-to-end.
        var stored = Session.Create(_cwd, "m");
        foreach (var m in BulkyHistoryWithToolCalls("src/first.ts"))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
            else if (m is ToolResultMessage tr) stored.AppendMessage(tr);
        }
        var storedId = stored.Id;

        var summaryProvider = StubProvider.Echo(
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
            }));
        var first = _factory.Resume(
            ConfigWith(summaryProvider, ("CompactionKeepRecentTokens", 50)), storedId);
        first.SubmitPrompt("hi");
        await WaitForAsync(() => !first.State.IsRunning);

        var firstCompaction = first.Storage.ReadAll().OfType<CompactionSessionEntry>().Single();
        await Assert.That(firstCompaction.Details).IsNotNull();
        await Assert.That(firstCompaction.Details!.ReadFiles).Contains("src/first.ts");

        // Append a second batch directly via storage so the resumed
        // session sees them as the "current" history (the first compaction
        // already cleared the original; we add fresh bulky content).
        var secondBatch = BulkyHistoryWithToolCalls("src/second.ts");
        foreach (var m in secondBatch)
        {
            if (m is UserMessage u) first.Storage.Append(new UserSessionEntry(u.Timestamp, u.Text));
            else if (m is AssistantMessage a)
                first.Storage.Append(new AssistantSessionEntry(a.Timestamp, a.Content, a.StopReason, a.Usage));
            else if (m is ToolResultMessage tr)
                first.Storage.Append(new ToolResultSessionEntry(
                    tr.Timestamp, tr.ToolCallId, tr.ToolName, tr.Content, tr.IsError));
        }

        var second = _factory.Resume(
            ConfigWith(summaryProvider, ("CompactionKeepRecentTokens", 50)), storedId);
        second.SubmitPrompt("hi2");
        await WaitForAsync(() => !second.State.IsRunning);

        // The second compaction rewrites the file; only the latest entry
        // remains. Its Details must union the first round's restored
        // files with the second round's tool-call paths.
        var compactions = second.Storage.ReadAll().OfType<CompactionSessionEntry>().ToList();
        await Assert.That(compactions.Count).IsGreaterThanOrEqualTo(1);
        var latest = compactions[^1];
        await Assert.That(latest.Details).IsNotNull();
        await Assert.That(latest.Details!.ReadFiles).Contains("src/first.ts");
        await Assert.That(latest.Details.ReadFiles).Contains("src/second.ts");
    }

    [Test]
    public async Task AutoCompact_FoldsSummaryUsageIntoSessionStats()
    {
        // The summary LLM call reports usage; that usage is NOT visible in
        // the post-compaction message list (the summary user-message and
        // kept-messages don't carry it), so without compensation the
        // SessionStats would underreport the session's billed totals. The
        // session adds the summary's usage to the live stats.
        var stored = Session.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;

        var summaryUsage = new Usage { Input = 100, Output = 50, TotalTokens = 150, CacheRead = 20 };
        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
                Usage = summaryUsage,
            }),
        };
        var resumed = _factory.Resume(
            ConfigWith(StubProvider.Echo(summaryEvents),
                ("CompactionKeepRecentTokens", 50)), storedId);

        resumed.SubmitPrompt("hi");
        await WaitForAsync(() => !resumed.State.IsRunning);

        // The post-compaction Stats must reflect the summary call's input
        // tokens even though no AssistantMessage in the live message list
        // carries that usage.
        await Assert.That(resumed.State.Stats.InputTokens).IsGreaterThanOrEqualTo(summaryUsage.Input);
    }

    [Test]
    public async Task Resume_RestoresAccumulatedSummaryUsage()
    {
        // A CompactionSessionEntry persists its summary Usage. After a
        // real compaction that records a known usage, a fresh resume of
        // the same session id must surface that usage in the live
        // SessionStats — otherwise the user would see the token total drop
        // after a session restart.
        var stored = Session.Create(_cwd, "m");
        foreach (var m in BulkyHistory(pairs: 6))
        {
            if (m is UserMessage u) stored.AppendMessage(u);
            else if (m is AssistantMessage a) stored.AppendMessage(a);
        }
        var storedId = stored.Id;

        var originalUsage = new Usage { Input = 200, Output = 80, TotalTokens = 280, CacheRead = 40 };
        var summaryProvider = StubProvider.Echo(
            new ProviderTextDeltaEvent("Summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("Summary")],
                StopReason = StopReasons.Stop,
                Usage = originalUsage,
            }));
        var first = _factory.Resume(
            ConfigWith(summaryProvider, ("CompactionKeepRecentTokens", 50)), storedId);
        first.SubmitPrompt("hi");
        await WaitForAsync(() => !first.State.IsRunning);

        // After the first compaction the live stats include the summary
        // usage — baseline for the resume-restore comparison.
        await Assert.That(first.State.Stats.InputTokens).IsGreaterThanOrEqualTo(originalUsage.Input);
        await Assert.That(first.State.Stats.OutputTokens).IsGreaterThanOrEqualTo(originalUsage.Output);

        // Resume the same session from disk in a brand-new instance.
        var resumed = _factory.Resume(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))), storedId);

        // The restored stats must include the summary usage we wrote
        // during the first compaction, otherwise the user sees the total
        // drop on every session restart.
        await Assert.That(resumed.State.Stats.InputTokens).IsGreaterThanOrEqualTo(originalUsage.Input);
        await Assert.That(resumed.State.Stats.OutputTokens).IsGreaterThanOrEqualTo(originalUsage.Output);
    }

    private static List<IAgentMessage> BulkyHistoryWithToolCalls(string readPath)
    {
        // Two pairs of user+assistant, the second carrying a real read tool
        // call against the supplied path. Big enough to force a cut inside
        // the second pair when keepRecentTokens=50.
        return
        [
            new UserMessage { Content = "u0 " + new string('u', 200) },
            new AssistantMessage { Content = [new TextBlock(new string('a', 400))], StopReason = StopReasons.Stop },
            new UserMessage { Content = "u1 " + new string('u', 200) },
            new AssistantMessage
            {
                Content =
                [
                    new TextBlock("reading"),
                    new ToolCall("t1", "read") { Arguments = new() { ["path"] = readPath } },
                ],
                StopReason = StopReasons.ToolUse,
            },
            new ToolResultMessage
            {
                ToolCallId = "t1", ToolName = "read",
                Content = [new TextBlock(new string('x', 400))],
            },
            new AssistantMessage { Content = [new TextBlock(new string('a', 400))], StopReason = StopReasons.Stop },
            new AssistantMessage { Content = [new TextBlock(new string('a', 400))], StopReason = StopReasons.Stop },
            new AssistantMessage { Content = [new TextBlock(new string('a', 400))], StopReason = StopReasons.Stop },
        ];
    }
}
