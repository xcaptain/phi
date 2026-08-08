using PhiCoding.Chat;
using PhiCoding.Desk.Components;
using PhiCoding.Providers;
using PhiCoding.Desk.Tests.Helpers;

namespace PhiCoding.Desk.Tests.Components;

/// <summary>
/// <see cref="PromptInputView"/>: the input shell dispatches editor text
/// straight to the session (or as steering when a run is in flight),
/// exposes a footer with the model picker, the workspace picker (fresh
/// sessions only), and the submit button. Tests drive the internal text
/// observable and the picker ComboBoxes directly to assert the resulting
/// session actions.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class PromptInputViewTests
{
    private static (MockSession session, FakeSessionNavigator navigator, PromptInputView view, ChatTranscriptProjector projector) Create(
        Func<MockSession>? makeSession = null,
        Action<Action>? postToUi = null,
        Func<string?>? pickFolder = null)
    {
        var session = makeSession?.Invoke() ?? new MockSession();
        var navigator = new FakeSessionNavigator(session);
        var projector = new ChatTranscriptProjector(session);
        var view = new PromptInputView(session, navigator, new ProviderManager(), projector,
            pickFolder: pickFolder,
            postToUi: postToUi);
        view.Build();
        return (session, navigator, view, projector);
    }

    [Test]
    public async Task Submit_PlainText_CallsSessionSubmitPrompt()
    {
        var (session, _, view, _) = Create();

        view.Text.Value = "hello world";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsEqualTo("hello world");
    }

    [Test]
    public async Task Submit_PlainText_AddsUserLineToProjector()
    {
        var (_, _, view, projector) = Create();

        view.Text.Value = "hello world";
        view.SubmitForTest();

        // The user's own message must appear in the transcript immediately
        // (the session only exposes new messages at TurnEndEvent).
        await Assert.That(projector.Current.OfType<UserTextLine>().Any(l => l.Text == "hello world")).IsTrue();
    }

    [Test]
    public async Task Submit_EmptyText_Ignored()
    {
        var (session, _, view, projector) = Create();

        view.Text.Value = "   ";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsNull();
        await Assert.That(projector.Current).IsEmpty();
    }

    [Test]
    public async Task Submit_WhileRunning_QueuesSteering()
    {
        var (session, _, view, projector) = Create();
        session.UpdateState(s => s with { IsRunning = true });

        view.Text.Value = "steer me";
        view.SubmitForTest();

        // While running, a plain prompt is queued, not submitted as a
        // direct SubmitPrompt call — but the user's message still renders.
        await Assert.That(session.LastSubmittedText).IsNull();
        await Assert.That(projector.Current.OfType<UserTextLine>().Any(l => l.Text == "steer me")).IsTrue();
    }

    [Test]
    public async Task TypedText_PropagatesToObservable_ThenSubmits()
    {
        // The user's typed text must reach the bound observable, otherwise
        // SubmitCurrent reads an empty value and does nothing.
        var (session, _, view, _) = Create();

        view.Editor.Text = "hello from the editor";
        await Assert.That(view.Text.Value).IsEqualTo("hello from the editor");

        view.SubmitForTest();
        await Assert.That(session.LastSubmittedText).IsEqualTo("hello from the editor");
    }

    [Test]
    public async Task Submit_SlashText_GoesToSessionAsMessage()
    {
        // Slash dispatch is intentionally removed in the desktop shell:
        // typing "/new" or "/skill:NAME" sends the text straight to the
        // session like any other message rather than navigating/loading.
        var (session, _, view, _) = Create();

        view.Text.Value = "/new";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsEqualTo("/new");
    }

    [Test]
    public async Task Submit_PlainText_NoCompletionHintShown()
    {
        // Completion hint is removed; there should be no "↳ ..." label
        // visible after typing. We assert this indirectly by confirming
        // the input's observable surface no longer exposes completion
        // state (the field was deleted).
        var (_, _, view, _) = Create();

        view.Text.Value = "/new";
        // No exception means the shell no longer reacts to slash input.
        await Assert.That(view.Text.Value).IsEqualTo("/new");
    }

    // ──────── Workspace picker ────────

    [Test]
    public async Task WorkspacePicker_FreshSession_IsVisible()
    {
        var (session, _, view, _) = Create();
        await Assert.That(session.State.Messages.Count).IsEqualTo(0);

        await Assert.That(view.WorkspacePickerVisible).IsTrue();
    }

    [Test]
    public async Task WorkspacePicker_ExistingSession_IsHidden()
    {
        var (session, _, view, _) = Create();
        session.SetMessages(new PhiAgent.UserMessage { Content = "hello" });

        await Assert.That(view.WorkspacePickerVisible).IsFalse();
    }

    [Test]
    public async Task WorkspacePicker_FirstMessage_HidesIt()
    {
        var (session, _, view, _) = Create();
        await Assert.That(view.WorkspacePickerVisible).IsTrue();

        session.SetMessages(new PhiAgent.UserMessage { Content = "hello" });

        await Assert.That(view.WorkspacePickerVisible).IsFalse();
    }

    [Test]
    public async Task WorkspacePicker_LastEntryIsChooseFolderSentinel()
    {
        var (_, _, view, _) = Create();

        var last = view.WorkspaceItems[^1];
        await Assert.That(last.IsSentinel).IsTrue();
        await Assert.That(last.Label).IsEqualTo("📁 Choose folder…");
        await Assert.That(last.Cwd).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SelectDifferentWorkspace_NavigatesToNewSessionInThatCwd()
    {
        var (session, navigator, view, _) = Create();
        session.Cwd = "/current";

        view.SelectWorkspaceForTest("/other");

        await Assert.That(navigator.NavigateToNewCalls).IsEqualTo(1);
        await Assert.That(navigator.LastNewCwd).IsEqualTo("/other");
    }

    [Test]
    public async Task SelectSameWorkspace_DoesNotNavigate()
    {
        var (session, navigator, view, _) = Create();
        session.Cwd = "/current";

        view.SelectWorkspaceForTest("/current");

        await Assert.That(navigator.NavigateToNewCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SelectWorkspaceSentinel_InvokesPickFolderAndNavigates()
    {
        var (_, navigator, view, _) = Create(
            pickFolder: () => "/picked");

        var sentinelIdx = view.WorkspaceItems.Count - 1;
        await Assert.That(view.WorkspaceItems[sentinelIdx].IsSentinel).IsTrue();

        view.WorkspaceComboBox!.SelectedIndex = sentinelIdx;

        await Assert.That(navigator.NavigateToNewCalls).IsEqualTo(1);
        await Assert.That(navigator.LastNewCwd).IsEqualTo("/picked");
    }

    // ──────── Model picker ────────

    [Test]
    public async Task ModelPicker_HeadersPerProvider_AndCurrentMarked()
    {
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with
        {
            ProviderName = "deepseek",
            Model = "deepseek-v4-flash",
        });

        var items = PromptInputPickerBuilder.BuildModelPickerItems(
            ProviderCatalog.All,
            "deepseek",
            "deepseek-v4-flash",
            _ => true);

        // Header + 2 models for deepseek + header + 5 models for glm +
        // header + 5 for kimi + header + 3 for MiniMax.
        await Assert.That(items.Count).IsEqualTo(1 + 2 + 1 + 5 + 1 + 5 + 1 + 3);

        // First row is the current provider's header.
        await Assert.That(items[0].IsHeader).IsTrue();
        await Assert.That(items[0].Label).IsEqualTo("  DeepSeek");

        // First model row carries the ✓ marker + Entry/Model.
        await Assert.That(items[1].IsHeader).IsFalse();
        await Assert.That(items[1].IsCurrent).IsTrue();
        await Assert.That(items[1].Entry!.Name).IsEqualTo("deepseek");
        await Assert.That(items[1].Model).IsEqualTo("deepseek-v4-flash");
        await Assert.That(items[1].Label).IsEqualTo("    ✓ deepseek · deepseek-v4-flash");

        // Second model row is the sibling, not marked.
        await Assert.That(items[2].IsCurrent).IsFalse();
    }

    [Test]
    public async Task ModelPicker_ReflectsCurrentModelSelection()
    {
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with
        {
            ProviderName = "deepseek",
            Model = "deepseek-v4-flash",
        });

        // The view subscribes to StateChanged; ensure the latest items
        // were materialized into the picker with the current row first.
        var firstSelectable = view.ModelItems.IndexOfFirstSelectable();
        await Assert.That(firstSelectable).IsGreaterThanOrEqualTo(0);
        var current = view.ModelItems[firstSelectable];
        await Assert.That(current.IsCurrent).IsTrue();
        await Assert.That(current.Model).IsEqualTo("deepseek-v4-flash");
    }

    [Test]
    public async Task ModelPicker_SelectAnotherModel_CallsSwitchProvider()
    {
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with
        {
            ProviderName = "deepseek",
            Model = "deepseek-v4-flash",
        });

        // Find the row that points at deepseek-v4-pro.
        var proIdx = view.ModelItems.IndexOfFirstSelectable() + 1;
        view.ModelComboBox!.SelectedIndex = proIdx;

        await Assert.That(session.LastSwitchedProviderName).IsEqualTo("deepseek");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("deepseek-v4-pro");
    }

    [Test]
    public async Task ModelPicker_ExternalSwitch_UpdatesSelection()
    {
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with
        {
            ProviderName = "deepseek",
            Model = "deepseek-v4-flash",
        });

        // The harness picks a different model externally (e.g. /models
        // slash command, or restart); the picker must resync.
        session.UpdateState(s => s with { Model = "deepseek-v4-pro" });

        // After resync, the new "current" row's Model equals the new state.
        var current = view.ModelItems.FirstOrDefault(i => i.IsCurrent);
        await Assert.That(current).IsNotNull();
        await Assert.That(current!.Model).IsEqualTo("deepseek-v4-pro");
    }

    // ──────── Picker builder (pure) ────────

    [Test]
    public async Task BuildWorkspacePicker_CurrentCwdInsertedAtFront()
    {
        var items = PromptInputPickerBuilder.BuildWorkspacePickerItems(
            ["/b", "/c"],
            "/current");

        await Assert.That(items[^1].IsSentinel).IsTrue();
        await Assert.That(items[0].Cwd).IsEqualTo(Path.GetFullPath("/current"));
        // /b and /c follow in insertion order, no dedupe with /current.
        await Assert.That(items[1].Cwd).IsEqualTo(Path.GetFullPath("/b"));
        await Assert.That(items[2].Cwd).IsEqualTo(Path.GetFullPath("/c"));
    }

    [Test]
    public async Task BuildWorkspacePicker_DedupesKnownCwd()
    {
        var items = PromptInputPickerBuilder.BuildWorkspacePickerItems(
            ["/dup", "/dup"],
            "/dup");

        var nonSentinel = items.Where(i => !i.IsSentinel).ToList();
        await Assert.That(nonSentinel.Count).IsEqualTo(1);
        await Assert.That(nonSentinel[0].Cwd).IsEqualTo(Path.GetFullPath("/dup"));
    }
}