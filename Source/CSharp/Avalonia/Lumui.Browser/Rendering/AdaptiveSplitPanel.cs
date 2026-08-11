using Avalonia;
using Avalonia.Controls;

namespace Lumui.Browser.Rendering;

public sealed class AdaptiveSplitPanel : Panel
{
    public Double PrimaryShare { get; set; } = 0.72D;

    public Double ColumnSpacing { get; set; } = 48D;

    public Double RowSpacing { get; set; } = 24D;

    public Double Breakpoint { get; set; } = 980D;

    public Double MinimumSecondaryWidth { get; set; } = 360D;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }
        Double width = Double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : Breakpoint;
        Boolean wide = UseWideLayout(
            width,
            out Double primaryWidth,
            out Double secondaryWidth);
        MeasureChildren(width, wide, primaryWidth, secondaryWidth);
        return new Size(width, DesiredHeight(wide));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }
        Boolean wide = UseWideLayout(
            finalSize.Width,
            out Double primaryWidth,
            out Double secondaryWidth);
        if (wide && Children.Count > 1)
        {
            Double rowHeight = Children.Count == 2
                ? finalSize.Height
                : Math.Max(
                    Children[0].DesiredSize.Height,
                    Children[1].DesiredSize.Height);
            Children[0].Arrange(new Rect(
                0D,
                0D,
                primaryWidth,
                rowHeight));
            Children[1].Arrange(new Rect(
                primaryWidth + ColumnSpacing,
                0D,
                secondaryWidth,
                rowHeight));
            ArrangeRemaining(finalSize.Width, rowHeight + RowSpacing);
            return finalSize;
        }
        Double y = 0D;
        foreach (Control child in Children)
        {
            Double height = child.DesiredSize.Height;
            child.Arrange(new Rect(0D, y, finalSize.Width, height));
            y += height + RowSpacing;
        }
        return finalSize;
    }

    private Boolean UseWideLayout(
        Double width,
        out Double primaryWidth,
        out Double secondaryWidth)
    {
        Double usableWidth = Math.Max(0D, width - ColumnSpacing);
        Double share = Math.Clamp(PrimaryShare, 0.5D, 0.85D);
        primaryWidth = usableWidth * share;
        secondaryWidth = usableWidth - primaryWidth;
        return Children.Count > 1
            && width >= Breakpoint
            && secondaryWidth >= MinimumSecondaryWidth;
    }

    private void MeasureChildren(
        Double width,
        Boolean wide,
        Double primaryWidth,
        Double secondaryWidth)
    {
        if (wide && Children.Count > 1)
        {
            Children[0].Measure(new Size(
                primaryWidth,
                Double.PositiveInfinity));
            Children[1].Measure(new Size(
                secondaryWidth,
                Double.PositiveInfinity));
            for (Int32 index = 2; index < Children.Count; index++)
            {
                Children[index].Measure(new Size(
                    width,
                    Double.PositiveInfinity));
            }
            return;
        }
        foreach (Control child in Children)
        {
            child.Measure(new Size(width, Double.PositiveInfinity));
        }
    }

    private Double DesiredHeight(Boolean wide)
    {
        if (!wide || Children.Count < 2)
        {
            Double stackedHeight = 0D;
            foreach (Control child in Children)
            {
                stackedHeight += child.DesiredSize.Height;
            }
            return stackedHeight
                + (RowSpacing * Math.Max(0, Children.Count - 1));
        }
        Double height = Math.Max(
            Children[0].DesiredSize.Height,
            Children[1].DesiredSize.Height);
        for (Int32 index = 2; index < Children.Count; index++)
        {
            height += RowSpacing + Children[index].DesiredSize.Height;
        }
        return height;
    }

    private void ArrangeRemaining(Double width, Double y)
    {
        for (Int32 index = 2; index < Children.Count; index++)
        {
            Double height = Children[index].DesiredSize.Height;
            Children[index].Arrange(new Rect(0D, y, width, height));
            y += height + RowSpacing;
        }
    }
}
