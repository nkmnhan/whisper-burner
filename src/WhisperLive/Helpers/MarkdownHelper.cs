using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace WhisperLive.Helpers;

/// <summary>
/// Attached property that parses a limited Markdown subset and populates a
/// <see cref="RichTextBlock"/> with typed inlines. No external packages required.
/// <para>
/// <b>Supported:</b> headings (#/##/###), bullet lists (- / *), numbered lists,
/// inline bold (**), italic (*), code (`), horizontal rules, pipe tables (rendered
/// as monospaced rows — not aligned columns). Escaped Markdown, nested formatting,
/// links, and fenced code blocks are <b>not</b> supported.
/// </para>
/// Usage: &lt;RichTextBlock helpers:MarkdownHelper.Text="{x:Bind Text}" /&gt;
/// </summary>
public static class MarkdownHelper
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(MarkdownHelper),
            new PropertyMetadata(null, OnTextChanged));

    public static string GetText(RichTextBlock obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(RichTextBlock obj, string value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBlock rtb) return;
        rtb.Blocks.Clear();
        if (e.NewValue is not string md || string.IsNullOrEmpty(md)) return;
        Render(rtb, md);
    }

    private static void Render(RichTextBlock rtb, string md)
    {
        var lines = md.ReplaceLineEndings("\n").Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Skip table separator rows: |---|:---:|---| etc.
            if (IsTableSeparator(line)) continue;

            // Table data row: | cell | cell |
            if (line.StartsWith('|') && line.EndsWith('|'))
            {
                rtb.Blocks.Add(BuildTableRow(line));
                continue;
            }

            // Headings
            if (line.StartsWith("### "))
            {
                rtb.Blocks.Add(BuildHeading(line[4..], 13, topMargin: 6));
                continue;
            }
            if (line.StartsWith("## "))
            {
                rtb.Blocks.Add(BuildHeading(line[3..], 14, topMargin: 8));
                continue;
            }
            if (line.StartsWith("# "))
            {
                rtb.Blocks.Add(BuildHeading(line[2..], 16, topMargin: 8));
                continue;
            }

            // Horizontal rules — render as empty spacing paragraph
            if (line is "---" or "***" or "___" || line.All(c => c == '-' || c == '*' || c == '_'))
            {
                rtb.Blocks.Add(new Paragraph { Margin = new Thickness(0, 4, 0, 4) });
                continue;
            }

            // Empty line → spacing paragraph
            if (string.IsNullOrEmpty(line))
            {
                rtb.Blocks.Add(new Paragraph { Margin = new Thickness(0, 2, 0, 2) });
                continue;
            }

            // Bullet list: "- text" or "* text"
            if (line.Length >= 2 && line[1] == ' ' && (line[0] == '-' || line[0] == '*'))
            {
                var para = new Paragraph { Margin = new Thickness(8, 0, 0, 2) };
                AddInlines(para.Inlines, "• " + line[2..]);
                rtb.Blocks.Add(para);
                continue;
            }

            // Numbered list: "1. text"
            var dotIdx = line.IndexOf(". ");
            if (dotIdx > 0 && dotIdx <= 3 && line[..dotIdx].All(char.IsDigit))
            {
                var para = new Paragraph { Margin = new Thickness(8, 0, 0, 2) };
                AddInlines(para.Inlines, line);
                rtb.Blocks.Add(para);
                continue;
            }

            // Plain paragraph
            var plain = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            AddInlines(plain.Inlines, line);
            rtb.Blocks.Add(plain);
        }
    }

    // Parse **bold**, *italic*, and `code` spans within a line.
    private static void AddInlines(InlineCollection inlines, string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            // Bold: **text**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    var b = new Bold();
                    b.Inlines.Add(new Run { Text = text[(i + 2)..end] });
                    inlines.Add(b);
                    i = end + 2;
                    continue;
                }
            }

            // Italic: *text* (but not **)
            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
            {
                var end = FindSingleStar(text, i + 1);
                if (end >= 0)
                {
                    var it = new Italic();
                    it.Inlines.Add(new Run { Text = text[(i + 1)..end] });
                    inlines.Add(it);
                    i = end + 1;
                    continue;
                }
            }

            // Inline code: `text`
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end >= 0)
                {
                    inlines.Add(new Run { Text = text[(i + 1)..end], FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas, Courier New") });
                    i = end + 1;
                    continue;
                }
            }

            // Advance to next special character
            var next = text.IndexOfAny(['*', '`'], i + 1);
            var literal = next < 0 ? text[i..] : text[i..next];
            if (literal.Length > 0)
                inlines.Add(new Run { Text = literal });
            i = next < 0 ? text.Length : next;
        }
    }

    private static int FindSingleStar(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
                return i;
        return -1;
    }

    private static Paragraph BuildHeading(string text, double fontSize, double topMargin)
    {
        var para = new Paragraph { Margin = new Thickness(0, topMargin, 0, 4) };
        var run = new Run { Text = text, FontSize = fontSize };
        var bold = new Bold();
        bold.Inlines.Add(run);
        para.Inlines.Add(bold);
        return para;
    }

    private static Paragraph BuildTableRow(string line)
    {
        var cells = line.Trim('|').Split('|')
                        .Select(c => c.Trim())
                        .ToArray();
        var para = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
        // Render as monospaced joined text — proportional fonts cannot align columns reliably.
        para.Inlines.Add(new Run
        {
            Text = string.Join("  |  ", cells),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas, Courier New"),
        });
        return para;
    }

    private static bool IsTableSeparator(string line)
    {
        var t = line.Trim();
        return t.StartsWith('|') && t.Length > 2 &&
               t.All(c => c is '|' or '-' or ':' or ' ');
    }
}
