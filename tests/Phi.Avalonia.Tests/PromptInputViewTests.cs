using Phi.Avalonia.Components;
using Phi.Avalonia.Tests.Helpers;
using Phi.Chat;
using Phi.Prompt;
using Phi.Providers;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="PromptInputView"/>: the input shell dispatches editor text
/// straight to the session (or as steering when a run is in flight),
/// exposes a footer with the model picker, the workspace picker (fresh
/// sessions only), and the submit button. Tests drive the editor text and
/// the picker ComboBoxes directly to assert the resulting session actions.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class PromptInputViewTests
{
    private static (MockSession session, ActiveSession active, PromptInputView view, ChatTranscriptProjector projector) Create(
        Action<Action>? postToUi = null,
        Func<Task<string?>>? pickFolder = null)
    {
        AvaloniaTestHost.EnsureInitialized();
        var session = new MockSession();
        var active = new ActiveSession(session);
        var projector = new ChatTranscriptProjector(session);
        // ProviderManager reads API keys from the credential store, not
        // from env vars — inject a fake credential store so HasApiKey
        // returns true regardless of the host's environment. CI runners
        // don't ship keys; dev boxes do. Either way the model picker's
        // ApplyModelSelection should fire SwitchProvider.
        var providers = new ProviderManager(
            credentials: new AllKeysCredentialStore());
        var view = new PromptInputView(session, active, providers, projector,
            pickFolder: pickFolder,
            postToUi: postToUi ?? (a => a()),
            dispatchToUi: a => a());
        return (session, active, view, projector);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task Submit_PlainText_CallsSessionSubmitPrompt()
    {
        var (session, _, view, _) = Create();

        view.Editor.Text = "hello world";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsEqualTo("hello world");
    }

    [Test]
    public async Task Submit_EmptyText_Ignored()
    {
        var (session, _, view, _) = Create();

        view.Editor.Text = "   ";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsNull();
    }

    [Test]
    public async Task Submit_WhileRunning_QueuesSteering()
    {
        var (session, _, view, projector) = Create();
        session.UpdateState(s => s with { IsRunning = true });

        view.Editor.Text = "steer me";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsNull();
        await Assert.That(session.SteeringMessages).Contains("steer me");
        await Assert.That(projector.Current.OfType<UserTextLine>().Any(l => l.Text == "steer me")).IsTrue();
    }

    [Test]
    public async Task Submit_SlashText_GoesToSessionAsMessage()
    {
        // Slash dispatch is intentionally removed: typing "/new" sends the
        // text straight to the session like any other message.
        var (session, _, view, _) = Create();

        view.Editor.Text = "/new";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsEqualTo("/new");
    }

    [Test]
    public async Task EditorEnter_Submits()
    {
        var (session, _, view, _) = Create();

        view.Editor.Text = "enter hello";
        // Simulate the Enter key down path through the view's handler.
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsEqualTo("enter hello");
    }

    [Test]
    public async Task EditorArrowKey_IsHandled_DoesNotBubbleToTreeView()
    {
        // Regression: pressing → in the editor used to bubble unhandled to
        // the SukiSideMenu (a TreeView whose OnKeyDown does ItemsView[0] on
        // an empty items source) → IndexOutOfRangeException crash. The editor
        // now marks directional keys handled so they stay local.
        var (_, _, view, _) = Create();
        var e = new global::Avalonia.Input.KeyEventArgs
        {
            Key = global::Avalonia.Input.Key.Right,
            RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
        };
        view.Editor.RaiseEvent(e);

        await Assert.That(e.Handled).IsTrue();
    }

    // ──────── Submit button state ────────

    [Test]
    public async Task Layout_PickersAndSubmitAreInsideRootBorder()
    {
        // The classic chat-input layout: editor + bottom toolbar (pickers,
        // submit) all live inside the single rounded Border that is Root.
        // They must NOT be siblings floating in a separate chrome.
        var (_, _, view, _) = Create();

        // view.Root is now the PromptInputLayout UserControl; walk into its
        // Content (1:8:1 reading-column Grid) to the rounded Border.
        var root = view.Root;
        await Assert.That(root).IsAssignableTo<global::Avalonia.Controls.ContentControl>();
        var grid = (global::Avalonia.Controls.Grid)((global::Avalonia.Controls.ContentControl)root).Content!;
        var outer = (global::Avalonia.Controls.Border)grid.Children[0];

        // Known structure: Border -> StackPanel { editor, DockPanel footer
        // { modelCombo, workspaceCombo, submitButton } }. Navigate it to
        // confirm everything lives inside Root's single chrome.
        var stack = (global::Avalonia.Controls.StackPanel)outer.Child!;
        var footer = (global::Avalonia.Controls.DockPanel)stack.Children[1];

        await Assert.That(ReferenceEquals(stack.Children[0], view.Editor)).IsTrue();
        await Assert.That(footer.Children.Contains(view.ModelComboBox)).IsTrue();
        await Assert.That(footer.Children.Contains(view.WorkspaceComboBox)).IsTrue();
        await Assert.That(footer.Children.Contains(view.SubmitButton)).IsTrue();
    }

    [Test]
    public async Task Layout_EditorIsBorderless_BlendsIntoChrome()
    {
        // The editor must not draw its own inner box: SukiUI's NoShadow
        // class keeps it transparent with no border (even on focus), so the
        // whole control reads as one input box.
        var (_, _, view, _) = Create();

        await Assert.That(view.Editor.Classes.Contains("NoShadow")).IsTrue();
        await Assert.That(view.Editor.AcceptsReturn).IsTrue();
    }

    [Test]
    public async Task Editor_NoShadowClass_KeepsTypingWorking()
    {
        // The editor stays visually inside the input box via SukiUI's
        // declarative NoShadow class (transparent, no border — including on
        // focus) instead of a custom template, and typing must still flow
        // through to the session when submitted.
        var (session, _, view, _) = Create();

        var editor = view.Editor;
        await Assert.That(editor.Classes.Contains("NoShadow")).IsTrue();

        editor.Text = "borderless works";
        view.SubmitForTest();
        await Assert.That(session.LastSubmittedText).IsEqualTo("borderless works");
    }

    [Test]
    public async Task SubmitButton_Idle_ShowsSendArrow()
    {
        var (_, _, view, _) = Create();

        await Assert.That(view.SubmitButton).IsNotNull();
        await Assert.That(view.SubmitIconKind).IsEqualTo(Material.Icons.MaterialIconKind.ArrowUpward);
    }

    [Test]
    public async Task SubmitButton_WhileRunning_ShowsStopAndClickCancels()
    {
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with { IsRunning = true });

        await Assert.That(view.SubmitIconKind).IsEqualTo(Material.Icons.MaterialIconKind.Stop);
        view.SubmitButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(
            global::Avalonia.Controls.Button.ClickEvent));
        await Assert.That(session.CancelCalled).IsTrue();
    }

    [Test]
    public async Task SubmitButton_BackToIdle_RestoresSendArrow()
    {
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with { IsRunning = true });
        await Assert.That(view.SubmitIconKind).IsEqualTo(Material.Icons.MaterialIconKind.Stop);

        session.UpdateState(s => s with { IsRunning = false });

        await Assert.That(view.SubmitIconKind).IsEqualTo(Material.Icons.MaterialIconKind.ArrowUpward);
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
        session.SetMessages(new Phi.Agent.UserMessage { Content = "hello" });

        await Assert.That(view.WorkspacePickerVisible).IsFalse();
    }

    [Test]
    public async Task WorkspacePicker_FirstMessage_HidesIt()
    {
        var (session, _, view, _) = Create();
        await Assert.That(view.WorkspacePickerVisible).IsTrue();

        session.SetMessages(new Phi.Agent.UserMessage { Content = "hello" });

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
        var (session, active, view, _) = Create();
        session.Cwd = "/current";
        // When the session's NewSessionAsync is called, hand the active a
        // new MockSession so the swap succeeds and we can count the call.
        session.OnNewSession = _ => new MockSession { Cwd = "/other" };

        view.SelectWorkspaceForTest("/other");

        await Assert.That(session.NewSessionCalls).IsEqualTo(1);
        await Assert.That(active.Current.Cwd).IsEqualTo("/other");
    }

    [Test]
    public async Task SelectSameWorkspace_DoesNotNavigate()
    {
        var (session, active, view, _) = Create();
        session.Cwd = "/current";
        session.OnNewSession = _ => new MockSession { Cwd = "/other" };

        view.SelectWorkspaceForTest("/current");

        await Assert.That(session.NewSessionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SelectWorkspaceSentinel_InvokesPickFolderAndNavigates()
    {
        var (session, active, view, _) = Create(
            pickFolder: () => Task.FromResult<string?>("/picked"));
        session.OnNewSession = cwd => new MockSession { Cwd = cwd };

        var sentinelIdx = view.WorkspaceItems.Count - 1;
        await Assert.That(view.WorkspaceItems[sentinelIdx].IsSentinel).IsTrue();

        view.WorkspaceComboBox!.SelectedIndex = sentinelIdx;

        await WaitForAsync(() => session.NewSessionCalls == 1);
        await Assert.That(active.Current.Cwd).IsEqualTo("/picked");
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

        await Assert.That(items.Count).IsEqualTo(1 + 2 + 1 + 5 + 1 + 5 + 1 + 3);

        await Assert.That(items[0].IsHeader).IsTrue();
        await Assert.That(items[0].Label).IsEqualTo("  DeepSeek");

        await Assert.That(items[1].IsHeader).IsFalse();
        await Assert.That(items[1].IsCurrent).IsTrue();
        await Assert.That(items[1].Entry!.Name).IsEqualTo("deepseek");
        await Assert.That(items[1].Model).IsEqualTo("deepseek-v4-flash");
        await Assert.That(items[1].Label).IsEqualTo("    ✓ deepseek · deepseek-v4-flash");

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

        var current = view.ModelItems.FirstOrDefault(i => i.IsCurrent);
        await Assert.That(current).IsNotNull();
        await Assert.That(current!.Model).IsEqualTo("deepseek-v4-flash");
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

        // DeepSeek is the current provider; its second model row is
        // deepseek-v4-pro (index = header(0) + flash(1) + pro(2)).
        view.ModelComboBox!.SelectedIndex = 2;

        await Assert.That(session.LastSwitchedProviderName).IsEqualTo("deepseek");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("deepseek-v4-pro");
    }

    [Test]
    public async Task ModelPicker_AfterSwitch_SelectionStaysOnChosenModel()
    {
        // Regression: selecting deepseek-v4-pro used to snap the combo back
        // to flash, because the state-sync picked the provider's first model
        // (IndexOfFirstSelectable) instead of the live model's row.
        var (session, _, view, _) = Create();
        session.UpdateState(s => s with
        {
            ProviderName = "deepseek",
            Model = "deepseek-v4-flash",
        });

        // header(0) + flash(1) + pro(2).
        view.ModelComboBox!.SelectedIndex = 2;

        var selected = (ModelPickerItem)view.ModelComboBox.SelectedItem!;
        await Assert.That(selected.Model).IsEqualTo("deepseek-v4-pro");
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

        session.UpdateState(s => s with { Model = "deepseek-v4-pro" });

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
