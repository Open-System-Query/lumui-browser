using Avalonia;
using Avalonia.Controls;

namespace Lumui.Browser.Rendering;

public sealed class AdaptiveGridPanel : Panel
{
    private Double _measuredWidth = Double.NaN;

    public Double MinimumItemWidth { get; set; } = 230D;

    public Int32 MaximumColumns { get; set; } = 4;

    public Double ColumnSpacing { get; set; } = 12D;

    public Double RowSpacing { get; set; } = 12D;

    public Double MinimumWidthForTwoColumns { get; set; }

    public Double MinimumWidthForThreeColumns { get; set; }

    public Boolean PreserveColumnWidth { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        Double width = ResolveWidth(availableSize.Width);
        Int32 columns = ColumnCount(width);
        Double itemWidth = ItemWidth(width, columns);
        Double totalHeight = MeasureRows(itemWidth, columns);
        _measuredWidth = width;
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        Double width = ResolveWidth(finalSize.Width);
        Int32 columns = ColumnCount(width);
        Double itemWidth = ItemWidth(width, columns);
        Double y = 0D;

        for (Int32 row = 0; row * columns < Children.Count; row++)
        {
            Double rowHeight = 0D;
            for (Int32 column = 0; column < columns; column++)
            {
                Int32 index = (row * columns) + column;
                if (index >= Children.Count)
                {
                    break;
                }
                rowHeight = Math.Max(
                    rowHeight,
                    Children[index].DesiredSize.Height);
            }
            for (Int32 column = 0; column < columns; column++)
            {
                Int32 index = (row * columns) + column;
                if (index >= Children.Count)
                {
                    break;
                }
                Double x = column * (itemWidth + ColumnSpacing);
                Children[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }
            y += rowHeight + RowSpacing;
        }

        return finalSize;
    }

    private Int32 ColumnCount(Double width)
    {
        Int32 maximum = Math.Max(
            1,
            PreserveColumnWidth
                ? MaximumColumns
                : Math.Min(MaximumColumns, Children.Count));
        if (!Double.IsFinite(width) || width <= 0D)
        {
            return 1;
        }
        Int32 columns = (Int32)Math.Floor(
            (width + ColumnSpacing)
            / (Math.Max(1D, MinimumItemWidth) + ColumnSpacing));
        columns = Math.Clamp(columns, 1, maximum);
        if (columns >= 2
            && MinimumWidthForTwoColumns > 0D
            && width < MinimumWidthForTwoColumns)
        {
            return 1;
        }
        if (columns >= 3
            && MinimumWidthForThreeColumns > 0D
            && width < MinimumWidthForThreeColumns)
        {
            return 2;
        }
        return columns;
    }

    private Double ResolveWidth(Double width)
    {
        if (Double.IsFinite(width))
        {
            return Math.Max(0D, width);
        }
        if (Double.IsFinite(Bounds.Width) && Bounds.Width > 0D)
        {
            return Bounds.Width;
        }
        if (Double.IsFinite(_measuredWidth) && _measuredWidth > 0D)
        {
            return _measuredWidth;
        }
        return Math.Max(1D, MinimumItemWidth);
    }

    private Double ItemWidth(Double width, Int32 columns)
    {
        Double spacing = ColumnSpacing * Math.Max(0, columns - 1);
        return Math.Max(0D, (width - spacing) / columns);
    }

    private Double MeasureRows(Double itemWidth, Int32 columns)
    {
        Double totalHeight = 0D;
        for (Int32 row = 0; row * columns < Children.Count; row++)
        {
            Double rowHeight = 0D;
            for (Int32 column = 0; column < columns; column++)
            {
                Int32 index = (row * columns) + column;
                if (index >= Children.Count)
                {
                    break;
                }
                Control child = Children[index];
                child.Measure(new Size(itemWidth, Double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            if (row > 0)
            {
                totalHeight += RowSpacing;
            }
            totalHeight += rowHeight;
        }
        return totalHeight;
    }
}
