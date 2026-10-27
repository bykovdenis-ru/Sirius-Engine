using Sirius.Engine.Models;

namespace Sirius.Engine.Layout;

/// <summary>
/// Aligns text tables by padding every cell to the widest value in its column.
/// The parser emits rows as text separated by a vertical bar; without this pass
/// columns drift because proportional fonts and heading styles change widths.
/// </summary>
public static class TableFormatter
{
    private const string Separator = "  │  ";
    private const int MaxColumnWidth = 48;

    public static int Align(List<BlockModel> blocks)
    {
        var aligned = 0;
        var start = 0;

        while (start < blocks.Count)
        {
            if (!IsTableRow(blocks[start]))
            {
                start++;
                continue;
            }

            var end = start;
            while (end < blocks.Count && IsTableRow(blocks[end])) end++;

            if (end - start >= 2)
            {
                aligned += AlignRun(blocks, start, end);
            }

            start = end;
        }

        return aligned;
    }

    private static int AlignRun(List<BlockModel> blocks, int start, int end)
    {
        var entries = new List<(BlockModel Block, List<List<SpanModel>> Cells)>();
        var widths = new List<int>();

        for (var i = start; i < end; i++)
        {
            var cells = SplitCells(blocks[i]);
            if (cells.Count <= 1) continue;

            for (var column = 0; column < cells.Count; column++)
            {
                var width = Math.Min(MaxColumnWidth, CellText(cells[column]).Length);
                if (column >= widths.Count) widths.Add(width);
                else if (width > widths[column]) widths[column] = width;
            }

            entries.Add((blocks[i], cells));
        }

        if (entries.Count < 2 || widths.Count < 2) return 0;

        var aligned = 0;
        foreach (var (block, cells) in entries)
        {
            var rebuilt = new List<SpanModel>();
            for (var column = 0; column < cells.Count; column++)
            {
                if (column > 0)
                {
                    rebuilt.Add(new SpanModel { Text = Separator });
                }

                var cell = cells[column];
                var text = CellText(cell);
                var target = column < widths.Count ? widths[column] : text.Length;
                var padding = Math.Max(0, target - text.Length);

                if (cell.Count == 0)
                {
                    if (padding > 0) rebuilt.Add(new SpanModel { Text = new string(' ', padding) });
                    continue;
                }

                for (var s = 0; s < cell.Count; s++)
                {
                    var span = cell[s];
                    if (s == cell.Count - 1 && padding > 0)
                    {
                        span.Text += new string(' ', padding);
                    }
                    rebuilt.Add(span);
                }
            }

            block.Spans = rebuilt;
            aligned++;
        }

        return aligned;
    }

    private static bool IsTableRow(BlockModel block) =>
        block.Kind.Equals("table_row", StringComparison.OrdinalIgnoreCase);

    private static List<List<SpanModel>> SplitCells(BlockModel block)
    {
        var cells = new List<List<SpanModel>> { new() };

        foreach (var span in block.Spans)
        {
            var text = span.Text;
            var index = text.IndexOf(Separator, StringComparison.Ordinal);
            if (index < 0)
            {
                cells[^1].Add(Clone(span, text));
                continue;
            }

            var from = 0;
            while (index >= 0)
            {
                if (index > from || index == 0)
                {
                    cells[^1].Add(Clone(span, text[from..index]));
                }

                cells.Add(new List<SpanModel>());
                from = index + Separator.Length;
                index = text.IndexOf(Separator, from, StringComparison.Ordinal);
            }

            if (from <= text.Length - 1)
            {
                cells[^1].Add(Clone(span, text[from..]));
            }
        }

        return cells;
    }

    private static SpanModel Clone(SpanModel source, string text) => new()
    {
        Text = text,
        Bold = source.Bold,
        Italic = source.Italic,
        Code = source.Code,
        Underline = source.Underline,
        Strike = source.Strike,
        Mark = source.Mark,
        Link = source.Link,
        Color = source.Color,
        Background = source.Background
    };

    private static string CellText(List<SpanModel> cell)
    {
        if (cell.Count == 0) return string.Empty;
        return string.Concat(cell.Select(s => s.Text)).Trim();
    }
}
