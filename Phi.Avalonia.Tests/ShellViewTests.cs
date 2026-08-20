using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Phi.Agent;
using Phi.Avalonia.Tests.Helpers;
using Phi.Providers;
using Phi.Sessions;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="ShellView"/>: two-pane shell hosting the chat page and
/// exposing navigation through the sessions list.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ShellViewTests
{
    private static (SessionNavigator navigator, ShellView shell) CreateNavigatorShell(string cwd)
    {
        AvaloniaTestHost.EnsureInitialized();

        var stub = new StubProvider((_, _) => Empty());
        var resolver = new MapResolver(stub);
        var providers = new ProviderManager();
        var factory = new SessionFactory(resolver);
        var env = new SessionConfig { Cwd = cwd, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);
        var shell = new ShellView(
            navigator,
            providers,
            dispatchToUi: a => a(),
            postToUi: a => a());
        return (navigator, shell);
    }

    private static async IAsyncEnumerable<ProviderEvent> Empty()
    {
        await Task.Yield();
        yield break;
    }

    [Test]
    public async Task InitialState_ShowsChat()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            await Assert.That(shell.ChatPage).IsNotNull();
            await Assert.That(shell.ViewHost.Content).IsEqualTo(shell.ChatPage!.Root);
        }
    }

    [Test]
    public async Task NavModel_Rebuild_AfterSessionPersists()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var session = navigator.Current;
            await Assert.That(session.State.IsPersisted).IsFalse();

            session.SubmitPrompt("hello");

            // The session persists asynchronously (first message writes the
            // index); the shell should rebuild the nav so the session row
            // appears and gets highlighted.
            await WaitForAsync(() =>
            {
                var entries = NavModel.BuildMainEntries(
                    WorkspaceSessionStore.ListAllSessions(7),
                    NavModel.GroupMode.ByWorkspace);
                return entries.Any(e =>
                    e.Kind == NavModel.Kind.Session
                    && e.SessionId == session.State.SessionId);
            });
        }
    }

    [Test]
    public async Task SwitchGroupMode_OnRealizedList_DoesNotThrow()
    {
        // Regression: reassigning ItemsSource on a live ListBox recycles its
        // containers, which used to invoke the item template with null data
        // and throw NullReferenceException (seen when clicking "By date").
        // Seed enough sessions that the list actually realizes containers.
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-ws-{Guid.NewGuid():N}");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var s = Session.Create(Path.GetTempPath(), "m");
                s.AppendMessage(new Phi.Agent.UserMessage { Content = $"seeded {i}" });
            }

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                var window = new Window
                {
                    Width = 800,
                    Height = 600,
                    Content = shell.Root,
                };
                window.Show();
                // Realize layout so the ListBox creates containers.
                Dispatcher.UIThread.RunJobs();

                shell.GroupMode = NavModel.GroupMode.ByDate;
                Dispatcher.UIThread.RunJobs();

                // ByDate → ByWorkspace also swaps the entries back.
                shell.GroupMode = NavModel.GroupMode.ByWorkspace;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(shell.SessionsList.ItemsSource).IsNotNull();
                window.Close();
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task Sidebar_IsThreeSections_WithDividersAndHeader()
    {
        // The sidebar must be three sections (New Chat / sessions browser /
        // Providers) using SukiSideMenu's natural slots:
        //   HeaderContent: [New Chat, divider, sessions header, sessions list]
        //   FooterContent: Providers
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            // shell.Root is now the ShellLayout UserControl built on
            // SukiSideMenu; the sessions browser lives in its HeaderContent
            // (a five-row Grid: New Chat / divider / sessions header /
            // sessions list / Providers).
            var sideMenu = (SukiUI.Controls.SukiSideMenu)((global::Avalonia.Controls.ContentControl)shell.Root).Content!;
            var pane = (global::Avalonia.Controls.Grid)sideMenu.HeaderContent!;
            await Assert.That(pane.RowDefinitions.Count).IsEqualTo(5);

            // Row 0 is the New Chat button, row 1 the divider.
            await Assert.That(pane.Children[0].GetType()).IsEqualTo(typeof(global::Avalonia.Controls.Button));
            await Assert.That(pane.Children[1].GetType()).IsEqualTo(typeof(global::Avalonia.Controls.Border));
            // Row 3 is the sessions list (star row).
            await Assert.That(ReferenceEquals(pane.Children[3], shell.SessionsList)).IsTrue();

            // The sessions header (row 2) is a two-column Grid: "会话" in
            // column 0, the icon-only toggle buttons in column 1.
            var header = (global::Avalonia.Controls.Grid)pane.Children[2];
            await Assert.That(header.ColumnDefinitions.Count).IsEqualTo(2);
            await Assert.That(header.Children.Count).IsEqualTo(2);
            var leftLabel = (global::Avalonia.Controls.TextBlock)header.Children[0];
            await Assert.That(leftLabel.Text).IsEqualTo("会话");
            var rightButtons = (global::Avalonia.Controls.StackPanel)header.Children[1];
            await Assert.That(rightButtons.Children.Count).IsEqualTo(2);
            await Assert.That(ReferenceEquals(rightButtons.Children[0], shell.ByDateButton)).IsTrue();
            await Assert.That(ReferenceEquals(rightButtons.Children[1], shell.ByWorkspaceButton)).IsTrue();

            // Toggle buttons are icon-only: their content is a MaterialIcon
            // (no text label), so they stay compact.
            await Assert.That(shell.ByDateButton.Content!.GetType().Name).IsEqualTo("MaterialIcon");
            await Assert.That(shell.ByWorkspaceButton.Content!.GetType().Name).IsEqualTo("MaterialIcon");

            // Providers sits pinned at the bottom of the bounded browser grid,
            // staying visible even in a short window.
            await Assert.That(ReferenceEquals(pane.Children[4], shell.ProvidersButton)).IsTrue();
        }
    }

    [Test]
    public async Task GroupToggle_DefaultIsWorkspace_AndSwitchesSelectionVisual()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            // Default group mode is ByWorkspace; the workspace toggle is the
            // active one.
            await Assert.That(shell.ByDateActive).IsFalse();

            // Switch to ByDate: the calendar toggle becomes the active one.
            shell.GroupMode = NavModel.GroupMode.ByDate;
            await Assert.That(shell.ByDateActive).IsTrue();

            // Selected visual: the active button carries the accent fill and
            // its icon uses the accent-contrast color, the inactive one is
            // transparent with a secondary icon.
            var dateIcon = (global::Material.Icons.Avalonia.MaterialIcon)shell.ByDateButton.Content!;
            await Assert.That(shell.ByDateButton.Background).IsEqualTo(Phi.Avalonia.AvaloniaTheme.Accent);
            await Assert.That(dateIcon.Foreground).IsEqualTo(Phi.Avalonia.AvaloniaTheme.AccentText);
        }
    }

    [Test]
    public async Task WorkspaceHeaderRows_NotSelectable_ButContainerEnabled()
    {
        // Workspace rows are group titles: selecting one must not resume a
        // session (highlight cleared). But the container stays ENABLED so
        // the row's ⋯ menu button remains clickable — disabling the
        // container would also disable the menu (regression).
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-ws-{Guid.NewGuid():N}");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            // Two workspaces so the ByWorkspace list has header rows.
            var cwdA = Path.Combine(Path.GetTempPath(), "phi-av-a");
            var cwdB = Path.Combine(Path.GetTempPath(), "phi-av-b");
            Directory.CreateDirectory(cwdA);
            Directory.CreateDirectory(cwdB);
            var sa = Session.Create(cwdA, "m");
            sa.AppendMessage(new Phi.Agent.UserMessage { Content = "a" });
            var sb = Session.Create(cwdB, "m");
            sb.AppendMessage(new Phi.Agent.UserMessage { Content = "b" });

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                var window = new Window
                {
                    Width = 800,
                    Height = 600,
                    Content = shell.Root,
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(shell.SessionsList.ItemsSource).IsNotNull();
                var entries = (System.Collections.IList)shell.SessionsList.ItemsSource!;
                await Assert.That(entries.Count).IsGreaterThanOrEqualTo(2);

                // Find the workspace header row index and its container.
                var headerIndex = -1;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is NavModel.Entry { Kind: NavModel.Kind.Workspace })
                    {
                        headerIndex = i;
                        break;
                    }
                }
                await Assert.That(headerIndex).IsGreaterThanOrEqualTo(0);

                var container = shell.SessionsList.ContainerFromIndex(headerIndex);
                await Assert.That(container).IsNotNull();
                // The container must stay enabled so the menu button works.
                await Assert.That(container!.IsEnabled).IsTrue();

                // Selecting the header must not navigate anywhere — the
                // current session stays put and the highlight clears.
                shell.SessionsList.SelectedIndex = headerIndex;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(shell.SessionsList.SelectedIndex).IsEqualTo(-1);

                window.Close();
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task SessionsList_HasNoNewChatRow()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var entries = (System.Collections.IList)shell.SessionsList.ItemsSource!;
            // No "New Chat" entry — the New Chat affordance lives only in the
            // top button (the enum has no NewChat kind at all).
            foreach (var e in entries)
                await Assert.That(e is NavModel.Entry { Title: "New Chat" }).IsFalse();
        }
    }

    // ──────── Session / workspace row management ────────

    [Test]
    public async Task SessionRow_ContainsEllipsisMenu_WithRenameAndDelete()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            // Build the row directly (no window/container realization needed):
            // it must carry a "⋮" menu with Rename + Delete.
            var row = shell.BuildSessionRow(new NavModel.Entry(NavModel.Kind.Session, "My session", "id-1"));
            var menu = FindEllipsisMenu(row);
            await Assert.That(menu).IsNotNull();
            await Assert.That(menu!.ItemCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task SessionRow_MenuIsInSeparateAutoColumn_NotOverlappingTitle()
    {
        // Regression: rows used a DockPanel with a Left-docked title sized to
        // its desired width, which overlapped the ⋯ menu on long titles. The
        // row is now a Grid: title fills a star column (ellipsizing), the menu
        // lives in a fixed Auto column so it's always clickable.
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var row = shell.BuildSessionRow(new NavModel.Entry(
                NavModel.Kind.Session, "a very long session title that would overflow", "id-1"));
            var grid = row;
            await Assert.That(grid.ColumnDefinitions.Count).IsEqualTo(2);
            await Assert.That(grid.ColumnDefinitions[0].Width).IsEqualTo(new GridLength(1, GridUnitType.Star));
            await Assert.That(grid.ColumnDefinitions[1].Width).IsEqualTo(GridLength.Auto);

            var title = (global::Avalonia.Controls.TextBlock)grid.Children[0];
            var menu = (Phi.Avalonia.Controls.EllipsisMenu)grid.Children[2];
            await Assert.That(Grid.GetColumn(title)).IsEqualTo(0);
            await Assert.That(Grid.GetColumn(menu)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task SessionRows_DoNotShowSukiCheckIcon()
    {
        // SukiUI's ListBoxItem theme draws a "✓" check on the selected row;
        // the selection background tint already marks it, so a local style
        // hides PathIcon#CheckSelected for the sessions list on every row.
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-check-{Guid.NewGuid():N}");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            var s = Session.Create(Path.GetTempPath(), "m");
            s.AppendMessage(new Phi.Agent.UserMessage { Content = "seed" });

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                var window = new Window { Width = 800, Height = 600, Content = shell.Root };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var container = shell.SessionsList.ContainerFromIndex(0);
                await Assert.That(container).IsNotNull();

                var check = container!
                    .GetVisualDescendants()
                    .OfType<global::Avalonia.Controls.PathIcon>()
                    .FirstOrDefault(p => p.Name == "CheckSelected");
                await Assert.That(check).IsNotNull();
                await Assert.That(check!.IsVisible).IsFalse();

                window.Close();
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task WorkspaceRow_ContainsEllipsisMenu_WithNewSessionAndDelete()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var row = shell.BuildWorkspaceRow(new NavModel.Entry(NavModel.Kind.Workspace, "~/proj"));
            var menu = FindEllipsisMenu(row);
            await Assert.That(menu).IsNotNull();
            await Assert.That(menu!.ItemCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task SessionRow_RenameTogglesWholeRow_BlurEndsEdit()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var entry = new NavModel.Entry(NavModel.Kind.Session, "Original", "id-1");
            var row = shell.BuildSessionRow(entry);

            // Children: title, renameBox, menu.
            var title = (global::Avalonia.Controls.TextBlock)row.Children[0];
            var renameBox = (global::Avalonia.Controls.TextBox)row.Children[1];
            var menu = (Phi.Avalonia.Controls.EllipsisMenu)row.Children[2];

            await Assert.That(title.IsVisible).IsTrue();
            await Assert.That(renameBox.IsVisible).IsFalse();
            await Assert.That(menu.IsVisible).IsTrue();

            // Entering rename mode hides the title + menu so the edit field
            // fills the whole row.
            shell.BeginRename(entry, title, renameBox, menu);
            await Assert.That(title.IsVisible).IsFalse();
            await Assert.That(menu.IsVisible).IsFalse();
            await Assert.That(renameBox.IsVisible).IsTrue();

            // Blur with an unchanged title must STILL end the edit (regression:
            // the row used to stay stuck in edit mode when nothing changed).
            renameBox.Text = entry.Title;
            shell.EndRename(entry, title, renameBox, menu, commit: true);
            await Assert.That(renameBox.IsVisible).IsFalse();
            await Assert.That(title.IsVisible).IsTrue();
            await Assert.That(menu.IsVisible).IsTrue();
        }
    }

    [Test]
    public async Task SessionRow_OutsidePointerPress_CommitsRename()
    {
        // Regression: clicking anywhere outside the edit field (including on
        // non-focusable empty space, which doesn't move keyboard focus) must
        // commit the rename and exit edit mode.
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-rename-{Guid.NewGuid():N}");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            var s = Session.Create(Path.GetTempPath(), "m");
            s.AppendMessage(new Phi.Agent.UserMessage { Content = "seed" });

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                var entry = new NavModel.Entry(NavModel.Kind.Session, "Original", s.Id);
                var row = shell.BuildSessionRow(entry);
                var window = new Window { Content = row };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var title = (global::Avalonia.Controls.TextBlock)row.Children[0];
                var renameBox = (global::Avalonia.Controls.TextBox)row.Children[1];
                var menu = (Phi.Avalonia.Controls.EllipsisMenu)row.Children[2];

                shell.BeginRename(entry, title, renameBox, menu);
                renameBox.Text = "Renamed by outside click";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(renameBox.IsVisible).IsTrue();

                // A pointer press on the row background (outside the TextBox)
                // must commit the rename and leave edit mode.
                PointerInputSimulator.LeftClick(row);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(renameBox.IsVisible).IsFalse();
                await Assert.That(title.IsVisible).IsTrue();
                await Assert.That(menu.IsVisible).IsTrue();
                window.Close();
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task SessionRow_EditFieldIsFlush_FillsWholeRow()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var row = shell.BuildSessionRow(new NavModel.Entry(NavModel.Kind.Session, "T", "id-1"));
            var renameBox = (global::Avalonia.Controls.TextBox)row.Children[1];

            // The edit field must look like the row itself: transparent, no
            // border, so it doesn't read as a separate input box inside the row.
            await Assert.That(renameBox.Background).IsEqualTo(global::Avalonia.Media.Brushes.Transparent);
            await Assert.That(renameBox.BorderThickness.Left).IsEqualTo(0);
        }
    }

    [Test]
    public async Task RenameSessionById_UpdatesStoreAndRebuilds()
    {
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-mgmt-{Guid.NewGuid():N}");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            var sa = Session.Create(Path.GetTempPath(), "m");
            sa.AppendMessage(new Phi.Agent.UserMessage { Content = "hello" });
            var id = sa.Id;

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                shell.RenameSessionById(id, "Renamed title");

                await WaitForAsync(() =>
                    WorkspaceSessionStore.FindSession(id)?.Title == "Renamed title");
                await Assert.That(WorkspaceSessionStore.FindSession(id)!.Title).IsEqualTo("Renamed title");
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task DeleteSessionRow_RemovesFromStore_AndRebuilds()
    {
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-mgmt-{Guid.NewGuid():N}");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            var sa = Session.Create(Path.GetTempPath(), "m");
            sa.AppendMessage(new Phi.Agent.UserMessage { Content = "hello" });
            var id = sa.Id;

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                await Assert.That(WorkspaceSessionStore.FindSession(id)).IsNotNull();

                shell.DeleteSessionRow(new NavModel.Entry(NavModel.Kind.Session, "x", id));

                await WaitForAsync(() => WorkspaceSessionStore.FindSession(id) is null);
                await Assert.That(WorkspaceSessionStore.FindSession(id)).IsNull();
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task DeleteWorkspaceRow_RemovesAllInCwd()
    {
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-mgmt-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), "phi-av-ws-del");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            Directory.CreateDirectory(cwd);
            var sa = Session.Create(cwd, "m");
            sa.AppendMessage(new Phi.Agent.UserMessage { Content = "a" });
            var sb = Session.Create(cwd, "m");
            sb.AppendMessage(new Phi.Agent.UserMessage { Content = "b" });

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                shell.DeleteWorkspaceRow(cwd);

                await WaitForAsync(() => WorkspaceSessionStore.FindSession(sa.Id) is null);
                await Assert.That(WorkspaceSessionStore.FindSession(sa.Id)).IsNull();
                await Assert.That(WorkspaceSessionStore.FindSession(sb.Id)).IsNull();
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    [Test]
    public async Task NewSessionInWorkspace_NavigatesToResolvedCwd()
    {
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-mgmt-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), "phi-av-ws-new");
        var previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = phiHome;
        try
        {
            Directory.CreateDirectory(cwd);
            var sa = Session.Create(cwd, "m");
            sa.AppendMessage(new Phi.Agent.UserMessage { Content = "a" });

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                shell.NewSessionInWorkspace(cwd);

                await WaitForAsync(() => navigator.Current.State.SessionId != sa.Id);
                await Assert.That(Path.GetFullPath(navigator.Current.Cwd))
                    .IsEqualTo(Path.GetFullPath(cwd));
            }
        }
        finally
        {
            SessionPaths.PhiHome = previousPhiHome;
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
    }

    private static Phi.Avalonia.Controls.EllipsisMenu? FindEllipsisMenu(global::Avalonia.Controls.Panel row)
    {
        foreach (var child in row.Children)
        {
            if (child is Phi.Avalonia.Controls.EllipsisMenu menu)
                return menu;
        }
        return null;
    }

    [Test]
    public async Task NavButtons_AreFlatByDefault_WithHoverBackground()
    {
        // New Chat / Providers must be transparent by default (matching
        // session list rows) and only gain a background while hovered.
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            foreach (var button in new[] { shell.NewChatButton, shell.ProvidersButton })
            {
                await Assert.That(button.Background).IsEqualTo(global::Avalonia.Media.Brushes.Transparent);
                await Assert.That(button.BorderThickness.Left).IsEqualTo(0);
            }
        }
    }

    [Test]
    public async Task WorkspaceRow_UsesLeafTitle_AndKeepsFullCwd()
    {
        // Display-only shortening: the row shows the last folder segment, but
        // the menu actions still resolve the full path.
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var row = shell.BuildWorkspaceRow(new NavModel.Entry(
                NavModel.Kind.Workspace, "phi", Cwd: "/Users/me/github/phi"));

            var label = (global::Avalonia.Controls.TextBlock)row.Children[0];
            await Assert.That(label.Text).IsEqualTo("PHI");
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    /// <summary>Resolves every provider name to a single stub instance.</summary>
    private sealed class MapResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }
}
