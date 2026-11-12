using Drift;

public class MarkdownTests
{
    private static MdBlock One(string text) => Assert.Single(Markdown.Parse(text));

    private static string Plain(MdBlock block) => string.Concat(block.Spans.Select(s => s.Text));

    [Fact]
    public void Lines_of_text_are_one_paragraph_and_keep_their_breaks()
    {
        var block = One("First line\nsecond line");
        Assert.Equal(MdKind.Paragraph, block.Kind);
        Assert.Equal("First line\nsecond line", Plain(block));
    }

    [Fact]
    public void A_blank_line_starts_a_new_paragraph()
    {
        var blocks = Markdown.Parse("One\n\nTwo");
        Assert.Equal(new[] { "One", "Two" }, blocks.Select(Plain));
    }

    [Fact]
    public void Headings_have_their_level()
    {
        var block = One("## Getting started");
        Assert.Equal(MdKind.Heading, block.Kind);
        Assert.Equal(2, block.Level);
        Assert.Equal("Getting started", Plain(block));
    }

    [Fact]
    public void Bold_italic_and_code()
    {
        var spans = One("a **bold** and *italic* and `code` and __also bold__").Spans;
        Assert.Contains(spans, s => s.Text == "bold" && s.Style == MdStyle.Bold);
        Assert.Contains(spans, s => s.Text == "italic" && s.Style == MdStyle.Italic);
        Assert.Contains(spans, s => s.Text == "code" && s.Style == MdStyle.Code);
        Assert.Contains(spans, s => s.Text == "also bold" && s.Style == MdStyle.Bold);
    }

    [Fact]
    public void Italic_inside_bold()
    {
        var spans = One("**very *much* so**").Spans;
        Assert.Contains(spans, s => s.Text == "much" && s.Style == (MdStyle.Bold | MdStyle.Italic));
    }

    [Fact]
    public void Underscores_inside_words_are_just_underscores()
    {
        Assert.Equal("snake_case_name", Plain(One("snake_case_name")));
        Assert.All(One("snake_case_name").Spans, s => Assert.Equal(MdStyle.None, s.Style));
    }

    [Fact]
    public void Nothing_inside_code_is_formatting()
    {
        var spans = One("`a **b** c`").Spans;
        Assert.Equal("a **b** c", Assert.Single(spans).Text);
    }

    [Fact]
    public void An_unclosed_marker_is_shown_as_typed_while_the_answer_streams_in()
    {
        Assert.Equal("so **bo", Plain(One("so **bo")));
        Assert.All(One("so **bo").Spans, s => Assert.Equal(MdStyle.None, s.Style));
    }

    [Fact]
    public void Links_and_bare_addresses()
    {
        var spans = One("See [the docs](https://example.com/docs) or https://dotnet.microsoft.com.").Spans;
        Assert.Contains(spans, s => s.Text == "the docs" && s.Link == "https://example.com/docs");
        // The full stop after an address isn't part of it.
        Assert.Contains(spans, s => s.Text == "https://dotnet.microsoft.com" && s.Link == "https://dotnet.microsoft.com");
        Assert.EndsWith(".", Plain(One("See https://dotnet.microsoft.com.")));
    }

    [Fact]
    public void Backslash_escapes()
    {
        Assert.Equal("*not italic*", Plain(One(@"\*not italic\*")));
    }

    [Fact]
    public void Bullet_and_numbered_lists_with_nesting()
    {
        var blocks = Markdown.Parse("- one\n- two\n  - two a\n\n1. first\n2. second");
        Assert.Equal(5, blocks.Count);
        Assert.All(blocks.Take(3), b => Assert.Equal(MdKind.Bullet, b.Kind));
        Assert.Equal(new[] { 0, 0, 1 }, blocks.Take(3).Select(b => b.Level));
        Assert.Equal(MdKind.Numbered, blocks[3].Kind);
        Assert.Equal(2, blocks[4].Number);
        Assert.Equal("second", Plain(blocks[4]));
    }

    [Fact]
    public void A_code_block_keeps_its_text_exactly_and_its_language()
    {
        var block = One("```csharp\nvar x = **1**;\n  indented\n```");
        Assert.Equal(MdKind.Code, block.Kind);
        Assert.Equal("csharp", block.Language);
        Assert.Equal("var x = **1**;\n  indented", block.Code);
    }

    [Fact]
    public void A_code_block_still_streaming_in_is_already_code()
    {
        var block = One("```\nline one\nline t");
        Assert.Equal(MdKind.Code, block.Kind);
        Assert.Equal("line one\nline t", block.Code);
    }

    [Fact]
    public void Quotes_and_rules()
    {
        var blocks = Markdown.Parse("> wise words\n\n---\n\nafter");
        Assert.Equal(MdKind.Quote, blocks[0].Kind);
        Assert.Equal("wise words", Plain(blocks[0]));
        Assert.Equal(MdKind.Rule, blocks[1].Kind);
        Assert.Equal("after", Plain(blocks[2]));
    }

    private static string Cell(MdBlock table, int row, int column) => string.Concat(table.Rows![row][column].Select(s => s.Text));

    [Fact]
    public void A_table_has_its_header_rows_and_cells()
    {
        var table = One("| Name | Size |\n|---|---:|\n| **a** | 1 |\n| b | 22 |");
        Assert.Equal(MdKind.Table, table.Kind);
        Assert.Equal(3, table.Rows!.Count);
        Assert.Equal("Name", Cell(table, 0, 0));
        Assert.Equal("22", Cell(table, 2, 1));
        Assert.Equal(MdStyle.Bold, table.Rows[1][0].Single().Style);
        Assert.Equal(new[] { MdAlign.Left, MdAlign.Right }, table.Align);
    }

    [Fact]
    public void Tables_without_the_outer_pipes_and_with_centred_columns()
    {
        var table = One("a | b\n:-:|---\n1 | 2");
        Assert.Equal(MdKind.Table, table.Kind);
        Assert.Equal("2", Cell(table, 1, 1));
        Assert.Equal(MdAlign.Center, table.Align![0]);
    }

    [Fact]
    public void A_short_row_gets_empty_cells_and_a_long_one_is_cut_to_the_header()
    {
        var table = One("| a | b |\n|---|---|\n| 1 |\n| 1 | 2 | 3 |");
        Assert.Equal(2, table.Rows![1].Count);
        Assert.Equal("", Cell(table, 1, 1));
        Assert.Equal(2, table.Rows[2].Count);
    }

    [Fact]
    public void A_pipe_in_code_or_escaped_is_not_a_new_cell()
    {
        var table = One("| cmd | note |\n|---|---|\n| `a | b` | x \\| y |");
        Assert.Equal("a | b", Cell(table, 1, 0));
        Assert.Equal("x | y", Cell(table, 1, 1));
    }

    [Fact]
    public void A_table_ends_at_a_line_without_a_pipe()
    {
        var blocks = Markdown.Parse("| a |\n|---|\n| 1 |\nAfter it.");
        Assert.Equal(MdKind.Table, blocks[0].Kind);
        Assert.Equal("After it.", Plain(blocks[1]));
    }

    [Fact]
    public void Until_the_line_under_its_header_arrives_it_is_text()
    {
        Assert.Equal(MdKind.Paragraph, One("| a | b |").Kind);
    }

    [Fact]
    public void Windows_line_endings()
    {
        Assert.Equal(new[] { "One", "Two" }, Markdown.Parse("One\r\n\r\nTwo").Select(Plain));
    }
}
