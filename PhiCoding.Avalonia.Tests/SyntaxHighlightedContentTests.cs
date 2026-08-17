using Markdown.Avalonia.Full;
using PhiCoding.Avalonia.Components.ToolCards;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="SyntaxHighlightedContent"/>: renders a metadata header
/// line + a content body. Picks the renderer by file extension —
/// markdown code block (via <see cref="MarkdownScrollViewer"/> →
/// ColorTextBlock.Avalonia / AvaloniaEdit) for known code / data
/// extensions, mono TextBlock fallback otherwise. The point of these
/// tests is the <see cref="SyntaxHighlightedContent.DetectLanguage"/>
/// mapping plus the structure of the constructed body.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class SyntaxHighlightedContentTests
{
    [Test]
    public async Task DetectLanguage_KnownExtensions_MapToExpectedLanguageId()
    {
        AvaloniaTestHost.EnsureInitialized();

        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.cs")).IsEqualTo("csharp");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.py")).IsEqualTo("python");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.ts")).IsEqualTo("typescript");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.tsx")).IsEqualTo("tsx");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.js")).IsEqualTo("javascript");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.json")).IsEqualTo("json");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.md")).IsEqualTo("markdown");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.html")).IsEqualTo("html");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.css")).IsEqualTo("css");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.rs")).IsEqualTo("rust");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.go")).IsEqualTo("go");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.java")).IsEqualTo("java");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.kt")).IsEqualTo("kotlin");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.sql")).IsEqualTo("sql");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.sh")).IsEqualTo("bash");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.toml")).IsEqualTo("toml");
    }

    [Test]
    public async Task DetectLanguage_CaseInsensitive()
    {
        AvaloniaTestHost.EnsureInitialized();

        await Assert.That(SyntaxHighlightedContent.DetectLanguage("FOO.CS")).IsEqualTo("csharp");
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("Foo.Py")).IsEqualTo("python");
    }

    [Test]
    public async Task DetectLanguage_UnknownExtensions_ReturnNullForMonoFallback()
    {
        AvaloniaTestHost.EnsureInitialized();

        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.xyz")).IsNull();
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.log")).IsNull();
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("foo.txt")).IsNull();
    }

    [Test]
    public async Task DetectLanguage_NoPath_ReturnNull()
    {
        AvaloniaTestHost.EnsureInitialized();

        await Assert.That(SyntaxHighlightedContent.DetectLanguage(null)).IsNull();
        await Assert.That(SyntaxHighlightedContent.DetectLanguage("")).IsNull();
    }

    [Test]
    public async Task Constructor_HasMetadataHeaderAsFirstChild()
    {
        AvaloniaTestHost.EnsureInitialized();
        var view = new SyntaxHighlightedContent(
            header: "Foo.cs  ·  lines 1-3 of 100  ·  1.2KB",
            content: "line1\nline2\nline3",
            language: "csharp");

        await Assert.That(view.Children.Count).IsEqualTo(2);
        var headerText = (TextBlock)view.Children[0];
        await Assert.That(headerText.Text).IsEqualTo("Foo.cs  ·  lines 1-3 of 100  ·  1.2KB");
        await Assert.That(headerText.Foreground).IsEqualTo(AvaloniaTheme.TextSecondary);
    }

    [Test]
    public async Task Constructor_KnownLanguage_RendersMarkdownScrollViewer()
    {
        AvaloniaTestHost.EnsureInitialized();
        var view = new SyntaxHighlightedContent("h", "code", "csharp");

        await Assert.That(view.Children.Count).IsEqualTo(2);
        // Second child is the markdown viewer carrying the fenced code block.
        await Assert.That(view.Children[1]).IsAssignableFrom<MarkdownScrollViewer>();
    }

    [Test]
    public async Task Constructor_UnknownLanguage_RendersMonoTextBlock()
    {
        AvaloniaTestHost.EnsureInitialized();
        var view = new SyntaxHighlightedContent("h", "plain text", language: null);

        await Assert.That(view.Children.Count).IsEqualTo(2);
        var mono = (TextBlock)view.Children[1];
        await Assert.That(mono.Text).IsEqualTo("plain text");
        await Assert.That(mono.FontFamily).IsEqualTo(AvaloniaTheme.MonoFontFamily);
        await Assert.That(mono.Foreground).IsEqualTo(AvaloniaTheme.TextPrimary);
    }
}
