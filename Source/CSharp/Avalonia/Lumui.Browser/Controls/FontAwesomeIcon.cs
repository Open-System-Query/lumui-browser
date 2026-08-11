using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Optris.Icons.Avalonia.FontAwesome;
using Optris.Icons.Avalonia.Models;

namespace Lumui.Browser.Controls;

public sealed class FontAwesomeIcon : Control
{
    private const Double DefaultIconSize = 16D;
    private static readonly FontAwesomeIconProvider Provider =
        new FontAwesomeIconProvider();
    private static readonly ConcurrentDictionary<
        String,
        (Geometry Geometry, Rect ViewBox)> GeometryCache =
        new ConcurrentDictionary<
            String,
            (Geometry Geometry, Rect ViewBox)>(
                StringComparer.Ordinal);
    private Geometry? _geometry;
    private Rect _viewBox;
    private String _resolvedIcon = String.Empty;

    public static readonly StyledProperty<String> IconProperty =
        AvaloniaProperty.Register<FontAwesomeIcon, String>(
            nameof(Icon),
            String.Empty);

    public static readonly StyledProperty<Double> IconSizeProperty =
        AvaloniaProperty.Register<FontAwesomeIcon, Double>(
            nameof(IconSize),
            DefaultIconSize);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<FontAwesomeIcon, IBrush?>(
            nameof(Foreground));

    public FontAwesomeIcon()
    {
        Focusable = false;
        IsHitTestVisible = false;
        IsTabStop = false;
        AutomationProperties.SetName(this, String.Empty);
        AutomationProperties.SetHelpText(this, String.Empty);
    }

    public String Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value ?? String.Empty);
    }

    public Double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Double size = Math.Max(0D, IconSize);
        return new Size(size, size);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ResolveGeometry();
        if (_geometry is null
            || Bounds.Width <= 0D
            || Bounds.Height <= 0D
            || _viewBox.Width <= 0D
            || _viewBox.Height <= 0D)
        {
            return;
        }

        Double scale = Math.Min(
            Bounds.Width / _viewBox.Width,
            Bounds.Height / _viewBox.Height);
        Double x = (Bounds.Width - (_viewBox.Width * scale)) / 2D;
        Double y = (Bounds.Height - (_viewBox.Height * scale)) / 2D;
        Matrix transform =
            Matrix.CreateTranslation(-_viewBox.X, -_viewBox.Y)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(x, y);
        using (context.PushTransform(transform))
        {
            context.DrawGeometry(
                Foreground ?? Brushes.Black,
                null,
                _geometry);
        }
    }

    protected override void OnPropertyChanged(
        AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty)
        {
            _resolvedIcon = String.Empty;
            _geometry = null;
            _viewBox = default;
            InvalidateVisual();
        }
        else if (change.Property == IconSizeProperty)
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == ForegroundProperty)
        {
            InvalidateVisual();
        }
    }

    private void ResolveGeometry()
    {
        String icon = Icon;
        if (String.Equals(
                _resolvedIcon,
                icon,
                StringComparison.Ordinal))
        {
            return;
        }

        _resolvedIcon = icon;
        _geometry = null;
        _viewBox = default;
        if (String.IsNullOrWhiteSpace(icon))
        {
            return;
        }

        try
        {
            (Geometry Geometry, Rect ViewBox) resolved =
                GeometryCache.GetOrAdd(icon, LoadGeometry);
            _geometry = resolved.Geometry;
            _viewBox = resolved.ViewBox;
        }
        catch (Exception exception) when (
            exception is KeyNotFoundException
                or FormatException
                or InvalidOperationException)
        {
            Trace.TraceError(
                "Unable to render icon '{0}': {1}",
                icon,
                exception.Message);
        }
    }

    private static (Geometry Geometry, Rect ViewBox) LoadGeometry(
        String icon)
    {
        IconModel model = Provider.GetIcon(icon);
        Rect viewBox = new Rect(
            model.ViewBox.X,
            model.ViewBox.Y,
            model.ViewBox.Width,
            model.ViewBox.Height);
        if (viewBox.Width <= 0D || viewBox.Height <= 0D)
        {
            throw new FormatException(
                "The icon has an invalid view box.");
        }

        Geometry geometry = Geometry.Parse(model.Path.ToString());
        return (geometry, viewBox);
    }
}
