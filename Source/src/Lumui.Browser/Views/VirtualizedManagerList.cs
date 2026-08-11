using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace Lumui.Browser.Views;

internal sealed class VirtualizedManagerList : ScrollViewer
{
    public VirtualizedManagerList(IReadOnlyList<ManagerListItem> items)
    {
        ItemsControl list = new ItemsControl
        {
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<ManagerListItem>(
                (item, _) => item?.Create(),
                false),
            ItemsPanel = new FuncTemplate<Panel?>(() =>
                new VirtualizingStackPanel { CacheLength = 1D }),
            MaxWidth = 1120D,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Content = new Border
        {
            Padding = new Thickness(32D, 8D, 32D, 36D),
            Child = list
        };
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
    }
}
