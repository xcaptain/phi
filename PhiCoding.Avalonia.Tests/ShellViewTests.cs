using Avalonia.Controls;
using Avalonia.Threading;
using PhiAgent;
using PhiCoding.Avalonia.Tests.Helpers;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia.Tests;

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
        var factory = new CodingSessionFactory(resolver);
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
                var s = CodingSession.Create(Path.GetTempPath(), "m");
                s.AppendMessage(new PhiAgent.UserMessage { Content = $"seeded {i}" });
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
        // The sidebar must be three sections separated by dividers:
        //   0. New Chat button
        //   1. divider
        //   2. sessions header row ("会话" + icon-only toggle buttons)
        //   3. sessions list (star row)
        //   4. divider
        //   5. footer (Models / Providers)
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            // shell.Root is now the ShellLayout UserControl; walk into the
            // two-column Grid to reach the sidebar.
            var outer = (global::Avalonia.Controls.Grid)((global::Avalonia.Controls.ContentControl)shell.Root).Content!;
            var leftBorder = (global::Avalonia.Controls.Border)outer.Children[0];
            var pane = (global::Avalonia.Controls.Grid)leftBorder.Child!;
            await Assert.That(pane.RowDefinitions.Count).IsEqualTo(6);

            // Row 0 is the New Chat button.
            await Assert.That(pane.Children[0].GetType()).IsEqualTo(typeof(global::Avalonia.Controls.Button));
            // Rows 1 and 4 are the separators.
            await Assert.That(pane.Children[1].GetType()).IsEqualTo(typeof(global::Avalonia.Controls.Border));
            await Assert.That(pane.Children[4].GetType()).IsEqualTo(typeof(global::Avalonia.Controls.Border));
            // Row 3 is the sessions list.
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
            await Assert.That(shell.ByDateButton.Background).IsEqualTo(PhiCoding.Avalonia.AvaloniaTheme.Accent);
            await Assert.That(dateIcon.Foreground).IsEqualTo(PhiCoding.Avalonia.AvaloniaTheme.AccentText);
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
            var sa = CodingSession.Create(cwdA, "m");
            sa.AppendMessage(new PhiAgent.UserMessage { Content = "a" });
            var sb = CodingSession.Create(cwdB, "m");
            sb.AppendMessage(new PhiAgent.UserMessage { Content = "b" });

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
            var menu = (PhiCoding.Avalonia.Controls.EllipsisMenu)row.Children[2];

            await Assert.That(title.IsVisible).IsTrue();
            await Assert.That(renameBox.IsVisible).IsFalse();
            await Assert.That(menu.IsVisible).IsTrue();

            // Entering rename mode hides the title + menu so the edit field
            // fills the whole row.
            ShellView.BeginRename(entry, title, renameBox, menu);
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
            var sa = CodingSession.Create(Path.GetTempPath(), "m");
            sa.AppendMessage(new PhiAgent.UserMessage { Content = "hello" });
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
            var sa = CodingSession.Create(Path.GetTempPath(), "m");
            sa.AppendMessage(new PhiAgent.UserMessage { Content = "hello" });
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
            var sa = CodingSession.Create(cwd, "m");
            sa.AppendMessage(new PhiAgent.UserMessage { Content = "a" });
            var sb = CodingSession.Create(cwd, "m");
            sb.AppendMessage(new PhiAgent.UserMessage { Content = "b" });

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
            var sa = CodingSession.Create(cwd, "m");
            sa.AppendMessage(new PhiAgent.UserMessage { Content = "a" });

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

    private static PhiCoding.Avalonia.Controls.EllipsisMenu? FindEllipsisMenu(global::Avalonia.Controls.Panel row)
    {
        foreach (var child in row.Children)
        {
            if (child is PhiCoding.Avalonia.Controls.EllipsisMenu menu)
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
