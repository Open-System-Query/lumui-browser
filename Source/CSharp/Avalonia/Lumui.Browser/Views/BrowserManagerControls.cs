using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumui.Browser.Controls;
using Lumui.Browser.Presentation;

namespace Lumui.Browser.Views;

internal static class BrowserManagerControls
{
    public static StackPanel List() => new StackPanel
    {
        Spacing = 0D,
        MaxWidth = 1120D,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    public static ScrollViewer ScrollPage(Control content) => new ScrollViewer
    {
        Content = new Border
        {
            Padding = new Thickness(32D, 8D, 32D, 36D),
            Child = content
        },
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
    };

    public static Control VirtualizedPage(
        IReadOnlyList<ManagerListItem> items) =>
        new VirtualizedManagerList(items);

    public static TextBlock SectionLabel(String text)
    {
        TextBlock label = new TextBlock { Text = text };
        label.Classes.Add("section-label");
        return label;
    }

    public static Border EmptyState(String title, String message)
    {
        StackPanel content = new StackPanel { Spacing = 7D };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20D,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            Classes = { "subtle" },
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        Border border = new Border { Child = content };
        border.Classes.Add("empty-state");
        return border;
    }

    public static Border ItemRow(Control main, Control actions)
    {
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12D
        };
        content.Children.Add(main);
        Grid.SetColumn(actions, 1);
        content.Children.Add(actions);
        Border row = new Border { Child = content };
        row.Classes.Add("manager-row");
        return row;
    }

    public static Button ItemLink(
        String title,
        String detail,
        String initial,
        String iconClass,
        Action open)
    {
        Border icon = ItemIcon(initial, iconClass);
        StackPanel text = new StackPanel
        {
            Spacing = 3D,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15D,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = detail,
            Classes = { "subtle" },
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14D
        };
        content.Children.Add(icon);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        Button button = new Button { Content = content };
        button.Classes.Add("item-link");
        AutomationProperties.SetName(button, "Open " + title);
        button.Click += (_, _) => open();
        return button;
    }

    public static Border ItemIcon(String label, String iconClass)
    {
        TextBlock text = new TextBlock { Text = label };
        text.Classes.Add("item-icon-text");
        Border icon = new Border { Child = text };
        icon.Classes.Add("item-icon");
        if (iconClass.Length > 0)
        {
            icon.Classes.Add(iconClass);
        }
        return icon;
    }

    public static StackPanel Actions(params Control[] controls)
    {
        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2D,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (Control control in controls)
        {
            actions.Children.Add(control);
        }
        return actions;
    }

    public static Button IconButton(
        String icon,
        String label,
        Boolean danger = false)
    {
        Button button = new Button
        {
            Content = new FontAwesomeIcon
            {
                Icon = icon,
                IconSize = 16D
            }
        };
        button.Classes.Add("icon-action");
        if (danger)
        {
            button.Classes.Add("danger");
        }
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    public static Button TextButton(String label, Boolean danger = false)
    {
        Button button = new Button { Content = label };
        button.Classes.Add("text-action");
        if (danger)
        {
            button.Classes.Add("danger");
        }
        return button;
    }

    public static void PrepareDialog(Window owner, Window dialog)
    {
        BrowserWindowAppearance.Inherit(owner, dialog);
    }

    public static String Initial(String value)
    {
        foreach (Char character in value)
        {
            if (Char.IsLetterOrDigit(character))
            {
                return Char.ToUpperInvariant(character).ToString();
            }
        }
        return "•";
    }

    public static String AddressLabel(Uri address)
    {
        String path = address.AbsolutePath == "/" ? String.Empty : address.AbsolutePath;
        return address.Host + path;
    }
}
