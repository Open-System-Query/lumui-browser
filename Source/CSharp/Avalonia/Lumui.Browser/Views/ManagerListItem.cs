using Avalonia.Controls;

namespace Lumui.Browser.Views;

internal sealed class ManagerListItem
{
    private readonly Func<Control> _create;

    public ManagerListItem(Func<Control> create)
    {
        _create = create;
    }

    public Control Create() => _create();
}
