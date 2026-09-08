using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Phi.Avalonia.Desktop.ViewModels;

namespace Phi.Avalonia.Desktop.Components.ChatLines;

/// <summary>
/// One tool-call row in the transcript. Renders as a collapsible
/// border-card on desktop — heading (icon + one-line summary) always
/// visible, body collapses by default and expands on click. On TUI
/// the same line uses a different rendering path because terminals
/// can't mouse-click; the desktop client here gets the interactive
/// collapse UX.
/// <para>
/// <see cref="ToolName"/> discriminates Bash / Read / Edit. The view's
/// <c>IconKind</c> is header icon resolved from this string;
/// <see cref="Summary"/> is the header one-liner (command / file path /
/// diff summary); <see cref="SourcePath"/> drives language-fence
/// detection for Read; <see cref="BodyMarkdown"/> is the body content
/// formatted as a fenced code block (e.g. <c>```csharp</c> / <c>```diff</c>
/// / plain <c>```</c>) — <see cref="MarkView.Avalonia.MarkdownViewer"/>
/// renders it with TextMate syntax highlighting (registered in
/// <c>App.axaml.cs:Initialize</c> via
/// <c>MarkdownViewerDefaults.Extensions.AddTextMateHighlighting()</c>).
/// </para>
/// </summary>
public partial class ToolCardLineViewModel : ViewModelBase
{
    /// <summary>Friendly tool name shown in the header (e.g. "Bash").</summary>
    public string ToolName { get; }

    /// <summary>One-line header text (command, file path, diff summary).</summary>
    public string Summary { get; }

    /// <summary>
    /// Body content wrapped in a fenced code block. The ctor picks the
    /// language tag from <see cref="ToolName"/> (Edit → diff) and
    /// <see cref="SourcePath"/>'s extension (Read → csharp / xml / json / …);
    /// Bash leaves the fence untagged so MarkdownViewer shows plain
    /// monospace. Selection / copy work out of the box because
    /// <see cref="MarkView.Avalonia.MarkdownViewer"/>'s code blocks
    /// render via <c>SelectableTextBlock</c> internally.
    /// </summary>
    public string BodyMarkdown { get; }

    /// <summary>
    /// File path for Read / Edit (Bash leaves this empty). Drives
    /// language-fence detection for Read: <c>.cs</c> → <c>csharp</c>,
    /// <c>.axml</c> / <c>.xaml</c> → <c>xml</c>, <c>.json</c> → <c>json</c>,
    /// <c>.md</c> → <c>markdown</c>; anything else falls back to untagged
    /// fence.
    /// </summary>
    /// <remarks>
    /// Named <c>SourcePath</c> rather than <c>Path</c> so it doesn't shadow
    /// <see cref="System.IO.Path"/>.
    /// </remarks>
    public string SourcePath { get; }

    [ObservableProperty]
    private bool _isOpen;

    public MaterialIconKind IconKind => ToolName switch
    {
        "Bash" => MaterialIconKind.Console,
        "Read" => MaterialIconKind.FileDocumentOutline,
        "Edit" => MaterialIconKind.Pencil,
        _ => MaterialIconKind.CogOutline,
    };

    public IRelayCommand ToggleCommand { get; }

    public ToolCardLineViewModel(
        string toolName,
        string summary,
        string body,
        string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(body);
        ToolName = toolName;
        Summary = summary;
        SourcePath = sourcePath ?? string.Empty;
        BodyMarkdown = WrapAsFencedCodeBlock(toolName, body, SourcePath);
        IsOpen = false;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
    }

    /// <summary>
    /// Wraps the raw tool body in a fenced code block with a language
    /// tag picked from <paramref name="toolName"/> + <paramref name="sourcePath"/>.
    /// MarkdownViewer passes the language tag to TextMate which then
    /// applies the matching grammar's token coloring (keyword / string /
    /// comment / number / etc.) and, for <c>diff</c>, the +/- line tints.
    /// </summary>
    private static string WrapAsFencedCodeBlock(string toolName, string body, string sourcePath)
    {
        var lang = toolName switch
        {
            "Edit" => "diff",
            "Read" => DetectReadLanguage(sourcePath),
            _ => "",
        };
        var fence = lang.Length == 0 ? "```" : $"```{lang}";
        return $"{fence}\n{body}\n```";
    }

    /// <summary>
    /// Map a file extension to a TextMate grammar id. TextMateSharp
    /// ships with grammars for these; anything else falls through to an
    /// untagged fence (plain monospace, no per-token coloring).
    /// </summary>
    private static string DetectReadLanguage(string sourcePath)
    {
        var ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".xaml" or ".axml" or ".axml.cs" => "xml",
            ".json" => "json",
            ".md" => "markdown",
            ".ts" or ".tsx" or ".js" => "typescript",
            ".py" => "python",
            ".sh" or ".bash" => "bash",
            ".yaml" or ".yml" => "yaml",
            ".toml" => "toml",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".sql" => "sql",
            ".rs" => "rust",
            ".go" => "go",
            _ => "",
        };
    }
}
