using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MarkView.Avalonia;
using Phi.Avalonia.Desktop2.ViewModels;
using Phi.Avalonia.Desktop2.Views;

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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new ShellViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
