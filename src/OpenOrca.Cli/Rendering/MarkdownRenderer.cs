using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using Spectre.Console.Rendering;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;

namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Converts Markdig AST to Spectre.Console renderables for rich terminal display.
/// </summary>
public sealed class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public IRenderable Render(string markdown)
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        var rows = new List<IRenderable>();

        foreach (var block in doc)
        {
            var renderable = RenderBlock(block);
            if (renderable is not null)
                rows.Add(renderable);
        }

        return rows.Count == 0
            ? new Text("")
            : new Rows(rows);
    }

    public void RenderToConsole(string markdown)
    {
        var doc = Markdown.Parse(markdown, Pipeline);

        foreach (var block in doc)
        {
            RenderBlockToConsole(block);
        }
    }

    private void RenderBlockToConsole(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingText = GetInlineText(heading.Inline);
                var level = heading.Level;
                var color = level switch
                {
                    1 => "bold cyan",
                    2 => "bold blue",
                    3 => "bold yellow",
                    _ => "bold"
                };
                var prefix = new string('#', level);
                AnsiConsole.MarkupLine($"[{color}]{prefix} {Markup.Escape(headingText)}[/]");
                AnsiConsole.WriteLine();
                break;

            case ParagraphBlock para:
                var paraText = RenderInlines(para.Inline);
                AnsiConsole.MarkupLine(paraText);
                AnsiConsole.WriteLine();
                break;

            case FencedCodeBlock fenced:
                var code = GetCodeBlockText(fenced);
                var lang = fenced.Info ?? "";
                var header = string.IsNullOrEmpty(lang) ? "Code" : lang;
                var panel = new Panel(Markup.Escape(code))
                {
                    Header = new PanelHeader($" {header} "),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Grey),
                    Padding = new Padding(1, 0)
                };
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
                break;

            case CodeBlock codeBlock:
                var codeText = GetCodeBlockText(codeBlock);
                AnsiConsole.Write(new Panel(Markup.Escape(codeText))
                {
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Grey),
                    Padding = new Padding(1, 0)
                });
                AnsiConsole.WriteLine();
                break;

            case ListBlock list:
                RenderList(list, 0);
                AnsiConsole.WriteLine();
                break;

            case ThematicBreakBlock:
                AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("grey")));
                AnsiConsole.WriteLine();
                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is ParagraphBlock qPara)
                    {
                        var qText = RenderInlines(qPara.Inline);
                        AnsiConsole.MarkupLine($"[grey]│[/] {qText}");
                    }
                }
                AnsiConsole.WriteLine();
                break;

            case MarkdigTable mdTable:
                var spectreTable = BuildSpectreTable(mdTable);
                AnsiConsole.Write(spectreTable);
                AnsiConsole.WriteLine();
                break;

            case HtmlBlock:
                // Skip HTML blocks in terminal
                break;

            default:
                // Fallback: try to render as plain text
                if (block is LeafBlock leaf && leaf.Inline is not null)
                {
                    var text = RenderInlines(leaf.Inline);
                    AnsiConsole.MarkupLine(text);
                    AnsiConsole.WriteLine();
                }
                break;
        }
    }

    private void RenderList(ListBlock list, int indent)
    {
        var index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem) continue;

            var prefix = list.IsOrdered
                ? $"{new string(' ', indent * 2)}{index++}."
                : $"{new string(' ', indent * 2)}•";

            foreach (var child in listItem)
            {
                if (child is ParagraphBlock para)
                {
                    var text = RenderInlines(para.Inline);
                    AnsiConsole.MarkupLine($" {prefix} {text}");
                }
                else if (child is ListBlock subList)
                {
                    RenderList(subList, indent + 1);
                }
            }
        }
    }

    private IRenderable? RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingText = GetInlineText(heading.Inline);
                var level = heading.Level;
                var style = level switch
                {
                    1 => "bold cyan",
                    2 => "bold blue",
                    3 => "bold yellow",
                    _ => "bold"
                };
                return new Markup($"[{style}]{new string('#', level)} {Markup.Escape(headingText)}[/]");

            case ParagraphBlock para:
                return new Markup(RenderInlines(para.Inline));

            case FencedCodeBlock fenced:
                var code = GetCodeBlockText(fenced);
                var lang = fenced.Info ?? "";
                return new Panel(Markup.Escape(code))
                {
                    Header = string.IsNullOrEmpty(lang) ? null : new PanelHeader($" {lang} "),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Grey),
                    Padding = new Padding(1, 0)
                };

            case ThematicBreakBlock:
                return new Rule().RuleStyle(Style.Parse("grey"));

            case MarkdigTable mdTable:
                return BuildSpectreTable(mdTable);

            default:
                return null;
        }
    }

    private static string RenderInlines(ContainerInline? container)
    {
        if (container is null) return "";

        var parts = new List<string>();

        foreach (var inline in container)
        {
            parts.Add(RenderInline(inline));
        }

        return string.Join("", parts);
    }

    private static string RenderInline(Inline inline)
    {
        return inline switch
        {
            LiteralInline literal => Markup.Escape(literal.Content.ToString()),
            EmphasisInline emphasis when emphasis.DelimiterCount == 2 =>
                $"[bold]{RenderInlines(emphasis)}[/]",
            EmphasisInline emphasis when emphasis.DelimiterCount == 1 =>
                $"[italic]{RenderInlines(emphasis)}[/]",
            EmphasisInline emphasis =>
                $"[bold italic]{RenderInlines(emphasis)}[/]",
            CodeInline code =>
                $"[grey on grey23]{Markup.Escape(code.Content)}[/]",
            LinkInline link =>
                $"[blue underline]{Markup.Escape(GetInlineText(link))}[/] [grey]({Markup.Escape(link.Url ?? "")})[/]",
            LineBreakInline => "\n",
            HtmlInline html => Markup.Escape(html.Tag),
            _ => ""
        };
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null) return "";

        var parts = new List<string>();
        foreach (var inline in container)
        {
            parts.Add(inline switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                EmphasisInline emphasis => GetInlineText(emphasis),
                _ => ""
            });
        }
        return string.Join("", parts);
    }

    private static Spectre.Console.Table BuildSpectreTable(MarkdigTable mdTable)
    {
        var table = new Spectre.Console.Table()
            .Border(TableBorder.Rounded)
            .Expand();

        var isFirstRow = true;

        foreach (var row in mdTable)
        {
            if (row is not MarkdigTableRow tableRow) continue;

            if (isFirstRow)
            {
                // First row becomes column headers
                foreach (var cell in tableRow)
                {
                    if (cell is MarkdigTableCell tableCell)
                        table.AddColumn(new TableColumn($"[bold]{Markup.Escape(GetCellText(tableCell))}[/]"));
                }
                isFirstRow = false;
            }
            else
            {
                var cells = new List<string>();
                foreach (var cell in tableRow)
                {
                    if (cell is MarkdigTableCell tableCell)
                        cells.Add(Markup.Escape(GetCellText(tableCell)));
                }

                // Pad with empty strings if fewer cells than columns
                while (cells.Count < table.Columns.Count)
                    cells.Add("");

                table.AddRow(cells.ToArray());
            }
        }

        return table;
    }

    private static string GetCellText(MarkdigTableCell cell)
    {
        var parts = new List<string>();
        foreach (var block in cell)
        {
            if (block is ParagraphBlock para)
                parts.Add(GetInlineText(para.Inline));
            else if (block is LeafBlock leaf && leaf.Inline is not null)
                parts.Add(GetInlineText(leaf.Inline));
        }
        return string.Join(" ", parts);
    }

    private static string GetCodeBlockText(CodeBlock codeBlock)
    {
        var lines = codeBlock.Lines;
        var parts = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            parts.Add(lines.Lines[i].Slice.ToString());
        }

        // Trim trailing empty lines
        while (parts.Count > 0 && string.IsNullOrWhiteSpace(parts[^1]))
            parts.RemoveAt(parts.Count - 1);

        return string.Join("\n", parts);
    }
}
