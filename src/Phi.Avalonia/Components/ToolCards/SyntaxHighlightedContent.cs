using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MarkView.Avalonia;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace Phi.Avalonia.Components.ToolCards;

/// <summary>
/// Renders a tool result body that benefits from syntax highlighting
/// (typically a <c>read</c> file body). Picks a presentation by extension:
/// <list type="bullet">
/// <item>Known code / data extension (e.g. <c>.cs</c>, <c>.py</c>,
/// <c>.json</c>, <c>.md</c>): wraps the content in a fenced code
/// block and hands it to a <see cref="MarkdownViewer"/> — TextMate
/// highlighting (registered app-wide via
/// <see cref="MarkdownViewerDefaults"/>) colors the code.</item>
/// <item>Unknown extension: falls back to a mono <see cref="TextBlock"/>
/// so plain text (logs, configs, etc.) still renders readably.</item>
/// </list>
/// A small dim header line above the content carries path / line range /
/// size metadata so the user knows what slice of what file they're
/// looking at — important when <c>read</c> returned only a window.
/// </summary>
public sealed class SyntaxHighlightedContent : StackPanel
{
    public SyntaxHighlightedContent(string header, string content, string? language)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(content);
        Orientation = Orientation.Vertical;
        Spacing = 6;

        Children.Add(new TextBlock
        {
            Text = header,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = AvaloniaTheme.TextSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        if (language is not null)
        {
            var md = new MarkdownViewer
            {
                Markdown = $"```{language}\n{content}\n```",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Children.Add(md);
        }
        else
        {
            Children.Add(new TextBlock
            {
                Text = content,
                FontFamily = AvaloniaTheme.MonoFontFamily,
                Foreground = AvaloniaTheme.TextPrimary,
                TextWrapping = TextWrapping.NoWrap,
            });
        }
    }

    /// <summary>
    /// Maps a file path's extension to the language identifier used for
    /// the fenced code block (TextMate grammar). Returns <c>null</c> for
    /// unknown extensions so the caller can fall back to plain mono
    /// rendering.
    /// </summary>
    public static string? DetectLanguage(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".csx" => "csharp",
            ".py" => "python",
            ".pyi" => "python",
            ".ts" => "typescript",
            ".tsx" => "tsx",
            ".js" => "javascript",
            ".jsx" => "jsx",
            ".mjs" => "javascript",
            ".cjs" => "javascript",
            ".json" => "json",
            ".jsonc" => "json",
            ".xml" => "xml",
            ".xsd" => "xml",
            ".yml" or ".yaml" => "yaml",
            ".md" or ".markdown" => "markdown",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".sass" => "scss",
            ".less" => "less",
            ".rs" => "rust",
            ".go" => "go",
            ".java" => "java",
            ".kt" or ".kts" => "kotlin",
            ".swift" => "swift",
            ".rb" => "ruby",
            ".sql" => "sql",
            ".sh" or ".bash" or ".zsh" => "bash",
            ".ps1" => "powershell",
            ".toml" => "toml",
            ".ini" => "ini",
            ".proto" => "protobuf",
            _ => null,
        };
    }
}
