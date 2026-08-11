using Avalonia.Controls;
using Avalonia.Input;

namespace Lumui.Browser.Views;

internal interface IBrowserLibraryPage : IDisposable
{
    String Title { get; }

    String Description { get; }

    String SearchPlaceholder { get; }

    String Summary { get; }

    String? PrimaryActionText { get; }

    String? SecondaryActionText { get; }

    Boolean PrimaryActionEnabled { get; }

    Boolean SecondaryActionEnabled { get; }

    event Action? Changed;

    Control Build(String query);

    Task PrimaryActionAsync(Window owner);

    Task SecondaryActionAsync(Window owner);

    Boolean HandleKeyDown(Window owner, KeyEventArgs eventArgs);
}
