// Markdown.cs - his replies drawn the way they are written: headings, lists and to-do lists, quotes, tables, links, bold,
// italics and code, every code block with its language and a Copy button. A character escaped with \ and HTML's &amp;
// and <br> read as what they stand for. Drawn again as his words type out, so a block not yet finished draws as far as
// it has got.
//
// 1. Render(text)       the whole reply as a document, for a read only box that can still be selected and copied
// 2. Inlines(text)      one stretch of text: its bold, italics, struck out words, code and links
// 3. Code(language, text) a code block, its language over it and Copy beside that
// 4. Copy(text, label)  a small Copy button that says Copied for a moment once pressed, for code and whole replies
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static Onsay.Desktop.ChatWindow;

namespace Onsay.Desktop;

static class Markdown
{
    static readonly Regex Fence = new(@"^\s*(```|~~~)\s*([^\s`]*)");
    static readonly Regex Heading = new(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
    static readonly Regex Item = new(@"^(\s*)([-*+\u2022]|\d{1,9}[.)])\s+(.*)$");
    static readonly Regex Checkbox = new(@"^\[([ xX])\]\s+(.*)$");
    static readonly Regex Line = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$");
    static readonly Regex Quote = new(@"^\s*>\s?(.*)$");
    static readonly Regex Row = new(@"\|");
    static readonly Regex Divider = new(@"^\s*\|?(\s*:?-{2,}:?\s*\|)+\s*:?-{2,}:?\s*\|?\s*$|^\s*\|(\s*:?-{2,}:?\s*\|)+\s*$");

    // Code first, so nothing inside it is read as anything else. The group numbers are what Inlines reads.
    static readonly Regex Spans = new(
        @"(`+)(.+?)\1" +                                                 // 2: code
        @"|\*\*(.+?)\*\*" +                                              // 3: bold
        @"|__(.+?)__" +                                                  // 4: bold
        @"|~~(.+?)~~" +                                                  // 5: struck out
        @"|(?<![\w*])\*(?![\s*])(.+?)(?<![\s*])\*(?![\w*])" +            // 6: italics
        @"|(?<![\w_])_(?![\s_])(.+?)(?<![\s_])_(?![\w_])" +              // 7: italics
        @"|\[([^\]]+)\]\(\s*<?([^)\s>]+)>?\s*\)" +                       // 8, 9: a link's words and address
        @"|(https?://[^\s<>()]*[^\s<>().,;:!?'""])");                    // 10: a bare address
    static readonly Regex Escape = new(@"\\([\\`*_{}\[\]()#+\-.!|~>])");
    static readonly Regex Break = new(@"<br\s*/?>", RegexOptions.IgnoreCase);
    const char Hidden = '\uF000';   // an escaped character is kept as Hidden plus itself until it is drawn, so nothing reads it
    static readonly Thickness Gap = new(0, 0, 0, 10);

    ////////// 1. BLOCKS //////////

    // 1. Line by line. A list keeps going over a blank line while the next line is another item, and nests by indent.
    public static FlowDocument Render(string text)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = Ui, FontSize = 14, LineHeight = 22, TextAlignment = TextAlignment.Left };
        text = Break.Replace(text.Replace("\r\n", "\n"), "\n");
        text = Escape.Replace(text, m => $"{(char)(Hidden + m.Groups[1].Value[0])}");
        var lines = text.Split('\n');
        var words = new List<string>();
        var lists = new Stack<(int indent, List list)>();
        void Words()
        {
            if (words.Count > 0) doc.Blocks.Add(new Paragraph(Spanned(string.Join("\n", words))) { Margin = Gap });
            words.Clear();
        }
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            Match m;
            if (lists.Count > 0 && !Item.IsMatch(line) && !(line.Trim().Length > 0 && line.StartsWith("  ")))
            {
                bool more = line.Trim().Length == 0 && i + 1 < lines.Length && Item.IsMatch(lines[i + 1]);
                if (more) continue;
                lists.Clear();
            }
            if ((m = Fence.Match(line)).Success)
            {
                Words();
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].TrimStart().StartsWith(m.Groups[1].Value)) code.Add(lines[i]);
                doc.Blocks.Add(new BlockUIContainer(Code(m.Groups[2].Value, string.Join("\n", code))) { Margin = Gap });
            }
            else if ((m = Item.Match(line)).Success)
            {
                Words();
                int indent = m.Groups[1].Value.Replace("\t", "    ").Length;
                bool numbered = char.IsDigit(m.Groups[2].Value[0]);
                while (lists.Count > 0 && lists.Peek().indent > indent) lists.Pop();
                if (lists.Count == 0 || lists.Peek().indent < indent)
                {
                    var list = new List
                    {
                        MarkerStyle = numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = lists.Count == 0 ? Gap : new Thickness(0, 2, 0, 0),
                        Padding = new Thickness(22, 0, 0, 0),
                    };
                    if (numbered && int.TryParse(m.Groups[2].Value.TrimEnd('.', ')'), out var start)) list.StartIndex = Math.Max(1, start);
                    if (lists.Count == 0) doc.Blocks.Add(list);
                    else lists.Peek().list.ListItems.LastListItem?.Blocks.Add(list);
                    lists.Push((indent, list));
                }
                // A to-do item, - [ ] or - [x], has a box in place of its dot.
                var said = m.Groups[3].Value;
                var item = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                if (Checkbox.Match(said) is { Success: true } task)
                {
                    lists.Peek().list.MarkerStyle = TextMarkerStyle.None;
                    lists.Peek().list.Padding = new Thickness(lists.Count > 1 ? 22 : 0, 0, 0, 0);
                    item.Inlines.Add(new Run(task.Groups[1].Value == " " ? "\uE739  " : "\uE73A  ") { FontFamily = Icons, FontSize = 12, Foreground = Accent });
                    said = task.Groups[2].Value;
                }
                item.Inlines.Add(Spanned(said));
                lists.Peek().list.ListItems.Add(new ListItem(item));
            }
            else if (lists.Count > 0 && line.Trim().Length > 0)
            {
                // A line under an item, indented: more of that item.
                if (lists.Peek().list.ListItems.LastListItem?.Blocks.LastBlock is Paragraph p) p.Inlines.Add(Spanned(" " + line.Trim()));
            }
            else if ((m = Heading.Match(line)).Success)
            {
                Words();
                // The two biggest in the brand's heading font, the rest as bold words, all near the size of the text in a chat.
                int level = m.Groups[1].Value.Length;
                doc.Blocks.Add(new Paragraph(Spanned(m.Groups[2].Value))
                {
                    FontFamily = level <= 2 ? Display : Ui,
                    FontSize = level switch { 1 => 18, 2 => 16, _ => 14 },
                    LineHeight = level == 1 ? 24 : 22,
                    FontWeight = level <= 2 ? FontWeights.Bold : FontWeights.SemiBold,
                    Margin = new Thickness(0, doc.Blocks.Count > 0 ? 8 : 0, 0, 6),
                });
            }
            else if (Line.IsMatch(line) && words.Count == 0)
                doc.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = Rule }) { Margin = new Thickness(0, 4, 0, 12) });
            else if ((m = Quote.Match(line)).Success)
            {
                Words();
                var quoted = new List<string> { m.Groups[1].Value };
                while (i + 1 < lines.Length && Quote.Match(lines[i + 1]) is { Success: true } next)
                {
                    quoted.Add(next.Groups[1].Value);
                    i++;
                }
                doc.Blocks.Add(new Paragraph(Spanned(string.Join("\n", quoted)))
                {
                    Foreground = Ink2,
                    BorderBrush = Edge_,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 0, 0, 0),
                    Margin = Gap,
                });
            }
            else if (Row.IsMatch(line) && words.Count == 0 && i + 1 < lines.Length && Divider.IsMatch(lines[i + 1]))
            {
                Words();
                var rows = new List<string> { line };
                for (i += 2; i < lines.Length && Row.IsMatch(lines[i]); i++) rows.Add(lines[i]);
                i--;
                doc.Blocks.Add(Table(rows));
            }
            else if (line.Trim().Length == 0) Words();
            else words.Add(line.Trim());
        }
        Words();
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return doc;
    }

    // A table: its first row the header, each cell's text read like any other, every cell ruled. The pipes on its outer
    // edges may be left off.
    static Table Table(List<string> rows)
    {
        var cells = rows.Select(r =>
        {
            var row = r.Trim();
            if (row.StartsWith('|')) row = row[1..];
            if (row.EndsWith('|')) row = row[..^1];
            return row.Split('|').Select(c => c.Trim()).ToArray();
        }).ToList();
        int columns = cells.Max(c => c.Length);
        var table = new Table { CellSpacing = 0, Margin = Gap, BorderBrush = Rule, BorderThickness = new Thickness(1, 1, 0, 0), FontSize = 13 };
        for (int c = 0; c < columns; c++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (int r = 0; r < cells.Count; r++)
        {
            var row = new TableRow { Background = r == 0 ? Sunk : null, FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal };
            for (int c = 0; c < columns; c++)
                row.Cells.Add(new TableCell(new Paragraph(Spanned(c < cells[r].Length ? cells[r][c] : "")) { Margin = new Thickness(0) })
                {
                    BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(8, 4, 8, 4),
                });
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    ////////// 2. INLINES //////////

    // 2. A line break inside a paragraph stays a line break.
    static Span Spanned(string text)
    {
        var span = new Span();
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) span.Inlines.Add(new LineBreak());
            span.Inlines.AddRange(Inlines(parts[i]).ToList());
        }
        return span;
    }

    // Bold inside bold inside bold ends somewhere: a long run of * or _ stays words, instead of running out of stack.
    static IEnumerable<Inline> Inlines(string text, int depth = 0)
    {
        if (depth > 8)
        {
            yield return Plain(text);
            yield break;
        }
        int at = 0;
        foreach (Match m in Spans.Matches(text))
        {
            if (m.Index > at) yield return Plain(text[at..m.Index]);
            at = m.Index + m.Length;
            var g = m.Groups;
            // Code has a thin space at each end, so its shading reaches past its first and last letters.
            if (g[2].Success) yield return new Run($"\u2009{Shown(g[2].Value.Trim(), code: true)}\u2009") { FontFamily = Mono, FontSize = 12.5, Background = Sunk, Foreground = Ink };
            else if (g[3].Success || g[4].Success) yield return Wrap(new Span { FontWeight = FontWeights.SemiBold }, g[3].Success ? g[3].Value : g[4].Value, depth);
            else if (g[5].Success) yield return Wrap(new Span { TextDecorations = TextDecorations.Strikethrough }, g[5].Value, depth);
            else if (g[6].Success || g[7].Success) yield return Wrap(new Italic(), g[6].Success ? g[6].Value : g[7].Value, depth);
            else yield return Link(g[8].Success ? g[8].Value : g[10].Value, g[8].Success ? g[9].Value : g[10].Value, depth);
        }
        if (at < text.Length) yield return Plain(text[at..]);
    }

    // Words as they are drawn: an escaped character as itself, and &amp; and the like as what they stand for. In code both
    // stay as written.
    static string Shown(string text, bool code = false)
    {
        var shown = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            bool escaped = c > Hidden && c < Hidden + 128;
            if (escaped && code) shown.Append('\\');
            shown.Append(escaped ? (char)(c - Hidden) : c);
        }
        return code ? shown.ToString() : WebUtility.HtmlDecode(shown.ToString());
    }

    static Run Plain(string text) => new(Shown(text));

    static Span Wrap(Span span, string inner, int depth)
    {
        span.Inlines.AddRange(Inlines(inner, depth + 1).ToList());
        return span;
    }

    // A link opens in the browser. Only web and mail links are links: anything else stays words.
    static Inline Link(string words, string target, int depth)
    {
        if (Native.WebLink(target) is not { } uri) return Plain(words);
        var link = new Hyperlink { Foreground = Accent, TextDecorations = null, Cursor = Cursors.Hand, ToolTip = uri.ToString() };
        // Words with an address in them stay words: read again they would be a link, inside a link, for ever.
        if (words.Contains("://")) link.Inlines.Add(Plain(words));
        else Wrap(link, words, depth);
        link.Click += (_, _) => Native.Open(uri.ToString());
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        return link;
    }

    ////////// 3-4. CODE AND COPY //////////

    // 3. Wrapped rather than scrolled sideways, so all of it reads in the narrow card.
    static UIElement Code(string language, string text)
    {
        var head = new DockPanel { Margin = new Thickness(10, 4, 4, 0) };
        head.Children.Add(Right(Copy(text, "Copy")));
        head.Children.Add(new TextBlock { Text = language.Length > 0 ? language : "code", FontFamily = Mono, FontSize = 11, Foreground = Ink3, VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            Background = Page_,
            BorderBrush = Rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new StackPanel
            {
                Children =
                {
                    head,
                    new TextBox
                    {
                        Text = Shown(text, code: true),
                        IsReadOnly = true,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Foreground = Ink,
                        FontFamily = Mono,
                        FontSize = 12.5,
                        TextWrapping = TextWrapping.Wrap,
                        Padding = new Thickness(10, 2, 10, 10),
                    },
                },
            },
        };
    }

    // 4.
    public static FrameworkElement Copy(string text, string label)
    {
        var words = new TextBlock { Text = label, FontSize = 11.5, Foreground = Ink3, VerticalAlignment = VerticalAlignment.Center };
        var button = new Border
        {
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Copy to the clipboard",
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new TextBlock { Text = "\uE8C8", FontFamily = Icons, FontSize = 11, Foreground = Ink3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) }, words },
            },
        };
        Hover(button, Rule, Brushes.Transparent);
        button.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { return; }   // another app holds the clipboard
            words.Text = "Copied";
            await Task.Delay(1500);
            words.Text = label;
        };
        return button;
    }
}
