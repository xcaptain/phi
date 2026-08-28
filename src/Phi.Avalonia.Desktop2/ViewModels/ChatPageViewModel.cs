using CommunityToolkit.Mvvm.ComponentModel;
using Phi.Avalonia.Desktop2.Components.ChatLines;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Right-column chat region: header (session title / cwd / model chip),
/// transcript (per-line rendering in UI-2), prompt input (workspace /
/// model / send in UI-3). Status-bar info (idle/running, context usage,
/// model label) used to live as a separate row but UI-3 folded it into
/// the prompt input panel — idle/running is now a button-icon swap,
/// context usage and model name move to the header in a later polish.
/// </summary>
public partial class ChatPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _headerText = "New Chat";

    /// <summary>
    /// Mock transcript for UI-2 review. Five representative chat lines
    /// matching the project's plan: user question → assistant markdown →
    /// bash tool card → read tool card → edit tool card. Real transcript
    /// subscription (Phase: backend wiring) replaces this list with
    /// events from <c>ChatTranscriptProjector</c>; UI stays the same because
    /// each line type is rendered via <c>ViewLocator</c>.
    /// </summary>
    public IReadOnlyList<ViewModelBase> Transcript { get; } =
    [
        new UserTextLineViewModel("How do I add a TODO list to the Phi shell sidebar?"),

        new AssistantTextLineViewModel(
            "Let me check the layout, then we'll add one. *Looking at the structure…*"),

        new ToolCardLineViewModel(
            toolName: "Bash",
            summary: "$ ls Phi.Avalonia.Desktop2/Views",
            body: """
                ChatPageView.axaml
                ChatPageView.axml.cs
                MainWindow.axmal
                MainWindow.axmal.cs
                ShellView.axmal
                ShellView.axmal.cs
                SidebarView.axmal
                SidebarView.axmal.cs
                """,
            sourcePath: null),

        new ToolCardLineViewModel(
            toolName: "Read",
            summary: "Phi.Avalonia.Desktop2/Program.cs",
            // Long body so the body has more lines than the visible
            // chat area shows; the outer transcript ScrollViewer handles
            // the scroll. ToolCardLineViewModel ctor wraps the body in
            // a csharp fenced code block, so the MarkdownViewer /
            // TextMate grammar gives us real keyword/string/comment
            // coloring instead of our old per-line ColorizeDiff helper.
            body: """
                using Avalonia;
                using Avalonia.Controls.ApplicationLifetimes;
                using Avalonia.Markup.Xaml;

                // Composition root: Desktop entry point. Composes the
                // provider manager, the cross-session Phi.SessionEnvironment,
                // the initial Phi.Session, and the UI's ActiveSession holder,
                // then hands them to the Avalonia app and starts the classic
                // desktop lifetime. Session *switching* after this point is
                // owned by ISession.NewSessionAsync / ResumeAsync — the new
                // session is returned, the old one disposes itself, and the
                // ActiveSession holder fires its Changed event so the shell
                // rebuilds. No separate navigator.

                namespace Phi.Avalonia.Desktop2;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        AvaloniaXamlLoader.Load(this);
                        base.Initialize();
                    }

                    public override void OnFrameworkInitializationCompleted()
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow = new MainWindow
                            {
                                DataContext = new ShellViewModel(),
                            };
                        }
                        base.OnFrameworkInitializationCompleted();
                    }
                }
                """,
            sourcePath: "Phi.Avalonia.Desktop2/Program.cs"),

        new ToolCardLineViewModel(
            toolName: "Edit",
            summary: "Phi.Avalonia.Desktop2/Views/SidebarView.axml (+3 lines)",
            body: """
                @@ SidebarView.axml @@
                  <DockPanel LastChildFill="True">
                    <Button Content="+ New Chat" ... />

                +   <!-- TODO list section -->
                +   <TextBlock Text="TODOS"
                +              FontWeight="SemiBold"
                +              Margin="20,12,16,4"
                +              FontSize="11" />

                +   <ItemsControl ItemsSource="{Binding Todos}" />

                    <Button DockPanel.Dock="Bottom" Content="Providers" ... />
                  </DockPanel>
                """,
            sourcePath: "Phi.Avalonia.Desktop2/Views/SidebarView.axml"),
    ];

    public PromptInputViewModel PromptInput { get; }

    public ChatPageViewModel()
    {
        PromptInput = new PromptInputViewModel();
    }
}
