using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MarkView.Avalonia;
using Phi.Avalonia.Desktop.ViewModels;
using Phi.Avalonia.Desktop.Views;
using Phi.Providers;

namespace Phi.Avalonia.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // TextMate grammar + LightPlus/DarkPlus themes for fenced code
        // blocks inside every MarkdownViewer (transcript assistant text +
        // tool card bodies in UI-2). Without this call MarkdownViewer
        // falls back to plain monospace and loses the syntax coloring.
        MarkdownViewerDefaults.Extensions.AddTextMateHighlighting();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition.InitializeAsync was awaited in Program.Main
            // before the UI loop started, so Composition.ActiveSession
            // / Composition.Providers are guaranteed non-null here. The
            // shell takes both directly — the providers are shared with
            // the prompt input picker so saved keys show up as models
            // without an explicit refresh; the active session is the
            // chat page's data source and gets rebuilt on swap.
            desktop.MainWindow = new MainWindow
            {
                DataContext = new ShellViewModel(
                    Composition.ActiveSession,
                    Composition.Providers),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
