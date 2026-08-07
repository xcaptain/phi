using Aprillz.MewUI;
using PhiCoding.Desk.Tests.Helpers;
using PhiCoding.Providers;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// <see cref="DeskShell"/>: clicking "New Chat" or a session item must keep
/// the chat page (with its prompt editor) in the right-side view host.
/// Regression: the editor disappeared after these clicks.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class DeskShellTests
{
    private const double Width = 800;
    private const double Height = 600;

    private static DeskShell CreateShell(MockSession session, out FakeSessionNavigator navigator)
    {
        MewTestHost.EnsureBackend();
        navigator = new FakeSessionNavigator(session);
        return new DeskShell(navigator, new ProviderManager(), dispatchToUi: action => action());
    }

    private static void Layout(DeskChatPage page)
    {
        page.Root.Measure(new Size(Width, Height));
        page.Root.Arrange(new Rect(0, 0, Width, Height));
    }

    [Test]
    public async Task InitialView_IsChatPage()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
    }

    [Test]
    public async Task ClickNewChat_KeepsChatPageInViewHost()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out var navigator);
        // The chat page starts in the host.
        await Assert.That(shell.ViewHost.Content).IsNotNull();

        shell.Select(DeskNavModel.Kind.NewChat);

        // FakeNavigator fires SessionChanged → OnSessionChanged rebuilds the
        // chat page. The host must still hold a live chat page.
        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
        await Assert.That(navigator.NavigateToNewCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ClickNewChat_EditorStillRenders()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Select(DeskNavModel.Kind.NewChat);

        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        Layout(page!);
        // The prompt editor must have a non-zero height (not collapsed).
        await Assert.That(page!.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task ClickSession_KeepsChatPageInViewHost()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out var navigator);
        // A session entry with a resume id.
        shell.Select(DeskNavModel.Kind.Session, sessionId: "some-session");

        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
        await Assert.That(navigator.LastResumedId).IsEqualTo("some-session");
    }

    [Test]
    public async Task ClickModels_ThenClickNewChat_ShowsChatAgain()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Select(DeskNavModel.Kind.Models);

        shell.Select(DeskNavModel.Kind.NewChat);
        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
    }

    [Test]
    public async Task EditorRendersThroughFullShellTree()
    {
        // Lay out the ENTIRE shell (NavigationView content host → ViewHost →
        // chat page), not just the page. This exercises the same nesting the
        // real window uses; the editor must still have height.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Select(DeskNavModel.Kind.NewChat);
        var root = shell.BuildRoot();
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
        // The transcript area must also get the middle region.
        await Assert.That(page!.TranscriptRoot.RenderSize.Height).IsGreaterThan(100);
    }

    [Test]
    public async Task NavSelectionChange_KeepsEditorRendering()
    {
        // Drive the real NavigationView selection path: setting the nav's
        // SelectedIndex fires SelectionChanged → OnNavSelection → (deferred)
        // HandleSelection → navigation → SessionChanged rebuild. The chat
        // page + editor must still render after the whole chain.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Nav.SelectedIndex = 0;

        var root = shell.BuildRoot();
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task GroupMode_Switch_RebuildsNavigation()
    {
        // Switching the group mode (ByWorkspace → ByDate) must rebuild the
        // nav so the toggle row label updates and the workspace headers go
        // away in ByDate mode.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByWorkspace);

        shell.GroupMode = DeskNavModel.GroupMode.ByDate;
        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByDate);

        // After ByDate, the rendered entries should no longer contain any
        // Workspace group headers.
        var entries = DeskNavModel.BuildMainEntries(
            [],
            shell.GroupMode,
            DeskNavModel.PaneMode.Expanded);
        await Assert.That(entries.Any(e => e.Kind == DeskNavModel.Kind.Workspace)).IsFalse();
        await Assert.That(entries[1].Kind).IsEqualTo(DeskNavModel.Kind.ToggleRow);
        await Assert.That(entries[1].Title).IsEqualTo("By date ▾");
    }

    [Test]
    public async Task PaneMode_Switch_RebuildsNavigation()
    {
        // Switching PaneDisplayMode on the underlying NavigationView must
        // be observed by the shell (via the watcher) and reflected in the
        // shell's PaneMode + a rebuild. We drive the same code path the
        // user-toggle (hamburger button) takes.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        await Assert.That(shell.PaneMode).IsEqualTo(DeskNavModel.PaneMode.Expanded);

        // Flip to Compact and invoke the same handler the watcher would.
        shell.Nav.PaneDisplayMode = Aprillz.MewUI.Controls.PaneDisplayMode.Compact;
        shell.SimulatePaneDisplayChange();

        await Assert.That(shell.PaneMode).IsEqualTo(DeskNavModel.PaneMode.Compact);
    }

    [Test]
    public async Task PaneMode_Compact_BuildMainEntries_OnlyNewChat()
    {
        // The nav-model contract for Compact mode: just New Chat.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Nav.PaneDisplayMode = Aprillz.MewUI.Controls.PaneDisplayMode.Compact;
        shell.SimulatePaneDisplayChange();

        var entries = DeskNavModel.BuildMainEntries(
            [new PhiCoding.SessionRecord("a", "/cwd", "m", null, 0, 0)],
            shell.GroupMode,
            shell.PaneMode);
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Kind).IsEqualTo(DeskNavModel.Kind.NewChat);
    }

    [Test]
    public async Task ToggleRow_RendersAsButtonGroupWithUniformSizing()
    {
        // The toggle row is a DockPanel: a "会话" label + a right-docked
        // ButtonGroup with two Uniform-sized segments ("By date" /
        // "By workspace"), each a checkable SegmentButton. After a full
        // shell layout the ButtonGroup must be present in the visual tree
        // and one segment must reflect the active group mode.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        var root = shell.BuildRoot();
        root.Measure(new Size(800, 600));
        root.Arrange(new Rect(0, 0, 800, 600));

        var group = Find<Aprillz.MewUI.Controls.ButtonGroup>(root);
        await Assert.That(group).IsNotNull();
        await Assert.That(group!.RenderSize.Width).IsGreaterThan(0);
        await Assert.That(group.RenderSize.Height).IsGreaterThan(0);

        // Exactly one segment is checked: the active group mode
        // (ByWorkspace by default).
        var checkedCount = 0;
        VisitChildren(group, child =>
        {
            if (child is Aprillz.MewUI.Controls.SegmentButton seg && seg.IsChecked)
                checkedCount++;
        });
        await Assert.That(checkedCount).IsEqualTo(1);

        // The two segments carry the clock/folder glyphs (not the long
        // "By date" / "By workspace" text that inflated the toggle).
        var glyphs = new List<string>();
        VisitChildren(group, child =>
        {
            if (child is Aprillz.MewUI.Controls.TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                glyphs.Add(tb.Text);
        });
        await Assert.That(glyphs).IsEquivalentTo(["⏰", "📁"]);
    }

    [Test]
    public async Task ToggleRow_ClickSwitch_ThenSwitchBack()
    {
        // Regression: clicking "By date" then "By workspace" must be able to
        // switch back. The rebuilt toggle row must carry fresh click handlers
        // on its segments, otherwise the second click does nothing.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        var root = shell.BuildRoot();
        root.Measure(new Size(800, 600));
        root.Arrange(new Rect(0, 0, 800, 600));

        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByWorkspace);

        // Click "By date" (index 0) → switches to ByDate.
        var segments = CollectSegments(root);
        await Assert.That(segments.Count).IsEqualTo(2);
        ClickSegment(segments[0]);
        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByDate);

        // Re-layout + re-find the rebuilt toggle row's segments.
        root.Arrange(new Rect(0, 0, 800, 600));
        segments = CollectSegments(root);
        await Assert.That(segments.Count).IsEqualTo(2);

        // Click "By workspace" (index 1) → must switch back.
        ClickSegment(segments[1]);
        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByWorkspace);
    }

    [Test]
    public async Task ToggleRow_ClickActiveSegment_KeepsModeAndChecked()
    {
        // Regression: a mutually-exclusive switch must not self-toggle. Clicking
        // the already-active segment ("By workspace") must leave the mode AND
        // the checked highlight intact (no IsCheckable self-toggle), so the
        // switch stays usable in both directions.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        var root = shell.BuildRoot();
        root.Measure(new Size(800, 600));
        root.Arrange(new Rect(0, 0, 800, 600));

        // Active segment is "By workspace" (index 1).
        var segments = CollectSegments(root);
        await Assert.That(segments[1].IsChecked).IsEqualTo(true);

        // Click the active segment — mode must stay, highlight must stay.
        ClickSegment(segments[1]);
        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByWorkspace);

        // Switch to ByDate, then click the new active segment — same invariant.
        root.Arrange(new Rect(0, 0, 800, 600));
        segments = CollectSegments(root);
        ClickSegment(segments[0]);
        root.Measure(new Size(800, 600));
        root.Arrange(new Rect(0, 0, 800, 600));
        segments = CollectSegments(root);
        await Assert.That(segments[0].IsChecked).IsEqualTo(true);

        ClickSegment(segments[0]);
        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByDate);
        root.Arrange(new Rect(0, 0, 800, 600));
        segments = CollectSegments(root);
        await Assert.That(segments[0].IsChecked).IsEqualTo(true);

        // And switching back still works.
        ClickSegment(segments[1]);
        await Assert.That(shell.GroupMode).IsEqualTo(DeskNavModel.GroupMode.ByWorkspace);
    }

    [Test]
    public async Task KindSelector_WorkspaceAndToggleAreHeaders()
    {
        // Regression: workspace group headers and the toggle row must map to
        // NavigationItemKind.Header so they are not selectable/clickable like
        // session rows. (A stale `e is Kind.X` type-pattern against the enum
        // always returned Item, making every row clickable.)
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        var kind = shell.Nav.Pane.KindSelector;
        await Assert.That(kind).IsNotNull();

        static Aprillz.MewUI.Controls.NavigationItemKind KindOf(
            Func<object?, Aprillz.MewUI.Controls.NavigationItemKind> kind,
            DeskNavModel.Kind entryKind,
            string? sessionId = null)
            => kind(new DeskNavModel.Entry(entryKind, "t", sessionId));

        await Assert.That(KindOf(kind!, DeskNavModel.Kind.Workspace))
            .IsEqualTo(Aprillz.MewUI.Controls.NavigationItemKind.Header);
        await Assert.That(KindOf(kind!, DeskNavModel.Kind.Header))
            .IsEqualTo(Aprillz.MewUI.Controls.NavigationItemKind.Header);
        await Assert.That(KindOf(kind!, DeskNavModel.Kind.ToggleRow))
            .IsEqualTo(Aprillz.MewUI.Controls.NavigationItemKind.Header);

        // Sessions, New Chat and footer-ish rows remain selectable items.
        await Assert.That(KindOf(kind!, DeskNavModel.Kind.Session, "id"))
            .IsEqualTo(Aprillz.MewUI.Controls.NavigationItemKind.Item);
        await Assert.That(KindOf(kind!, DeskNavModel.Kind.NewChat))
            .IsEqualTo(Aprillz.MewUI.Controls.NavigationItemKind.Item);
    }

    private static T? Find<T>(Aprillz.MewUI.Controls.Element? root) where T : Aprillz.MewUI.Controls.Element
    {
        if (root is T self) return self;
        var found = default(T?);
        if (root is Aprillz.MewUI.IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                found = Find<T>(child);
                return found is null;
            });
        }
        return found;
    }

    private static void VisitChildren(
        Aprillz.MewUI.Controls.Element root,
        Action<Aprillz.MewUI.Controls.Element> visit)
    {
        visit(root);
        if (root is Aprillz.MewUI.IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                VisitChildren(child, visit);
                return true;
            });
        }
    }

    private static List<Aprillz.MewUI.Controls.SegmentButton> CollectSegments(Aprillz.MewUI.Controls.Element root)
    {
        var segments = new List<Aprillz.MewUI.Controls.SegmentButton>();
        VisitChildren(root, child =>
        {
            if (child is Aprillz.MewUI.Controls.SegmentButton seg)
                segments.Add(seg);
        });
        return segments;
    }

    /// <summary>Simulates a real mouse click on a segment (Activate is private).</summary>
    private static void ClickSegment(Aprillz.MewUI.Controls.SegmentButton seg)
    {
        var activate = typeof(Aprillz.MewUI.Controls.SegmentButton)
            .GetMethod("Activate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        activate.Invoke(seg, null);
    }
}
