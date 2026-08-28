using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MarkView.Avalonia;
using Phi.Avalonia.Desktop2.ViewModels;
using Phi.Avalonia.Desktop2.Views;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2;

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
            // UI prototype composition root: a fresh ProviderManager
            // (reads ~/.phi/credentials.json on first call) shared by
            // the shell so Providers page saves + chat picker reads
            // stay in sync. The wiring phase introduces Composition.cs
            // which owns the manager + ActiveSession + cwd defaults.
            var providers = new ProviderManager();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new ShellViewModel(providers),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
