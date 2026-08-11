using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using LumuiProtocol = Lumui.Client.LumuiProtocol;

namespace Lumui.Browser.Presentation;

public sealed class AppearanceStyler
{
    private readonly AppearanceDefinition _appearance;
    private readonly BrandDefinition _brand;

    public AppearanceStyler(
        AppearanceDefinition appearance,
        BrandDefinition brand)
    {
        _appearance = appearance;
        _brand = brand;
    }

    public Control Hero(Control content, DeviceProfileKind profile)
    {
        Grid root = new Grid
        {
            Background = UsesLumiStyle
                ? Brush(_appearance.Background)
                : HeroBrush(profile),
            ClipToBounds = true
        };
        if (!UsesLumiStyle && _brand.Motif == BrandMotif.Orbs)
        {
            AddOrbs(root, profile);
        }
        else if (!UsesLumiStyle && _brand.Motif != BrandMotif.None)
        {
            root.Children.Add(MotifLines());
        }
        root.Children.Add(content);
        return root;
    }

    public void ApplyCard(
        Border border,
        String role,
        String priority)
    {
        border.Background = UsesLumiStyle
            ? Brush(_appearance.Surface)
            : CardBrush(role);
        border.BorderBrush = BorderBrush(role);
        border.BorderThickness = UsesLumiStyle
            ? new Thickness(4D, 0D, 0D, 0D)
            : CardBorderThickness();
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : _appearance.CornerRadius);
        border.Padding = CardPadding();

        if (priority == LumuiProtocol.Priorities.Critical)
        {
            border.BorderThickness = UsesLumiStyle
                ? new Thickness(7D, 0D, 0D, 0D)
                : new Thickness(
                    Math.Max(3D, border.BorderThickness.Left),
                    Math.Max(3D, border.BorderThickness.Top),
                    Math.Max(3D, border.BorderThickness.Right),
                    Math.Max(3D, border.BorderThickness.Bottom));
        }
    }

    public void ApplySoftPanel(Border border)
    {
        border.Background = UsesLumiStyle
            ? Brush(_appearance.SurfaceAlternate)
            : _appearance.Kind switch
        {
            AppearanceKind.Aero or AppearanceKind.Aqua =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            AppearanceKind.Steampunk =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            _ => Brush(_appearance.SurfaceAlternate)
        };
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = UsesLumiStyle
            ? new Thickness(4D, 0D, 0D, 0D)
            : _appearance.Kind switch
        {
            AppearanceKind.Classic => new Thickness(2D),
            AppearanceKind.Steampunk => new Thickness(2D),
            _ => new Thickness(1D)
        };
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle
                ? 0D
                : Math.Max(0D, _appearance.CornerRadius - 3D));
        border.Padding = new Thickness(UsesLumiStyle ? 20D : 16D);
    }

    public void ApplyViewerCard(Border border)
    {
        border.Background = Brush(_appearance.Surface);
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = UsesLumiStyle
            ? new Thickness(4D, 0D, 0D, 0D)
            : new Thickness(1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(6D, _appearance.CornerRadius));
        border.Padding = new Thickness(UsesLumiStyle ? 24D : 22D);
    }

    public void ApplyComponentPanel(Border border, String? accent = null)
    {
        border.Background = Brush(_appearance.Surface);
        border.BorderBrush = Brush(accent ?? _appearance.Border);
        border.BorderThickness = UsesLumiStyle
            ? new Thickness(4D, 0D, 0D, 0D)
            : accent is null
                ? new Thickness(1D)
                : new Thickness(1D, 5D, 1D, 1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(16D, _appearance.CornerRadius));
        border.Padding = new Thickness(UsesLumiStyle ? 24D : 20D);
    }

    public void ApplyShowcaseCard(Border border)
    {
        border.Background = Brush(_appearance.Surface);
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = UsesLumiStyle
            ? new Thickness(4D, 0D, 0D, 0D)
            : new Thickness(1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(18D, _appearance.CornerRadius));
        border.Padding = new Thickness(UsesLumiStyle ? 24D : 20D);
    }

    public void ApplyPreviewFrame(Border border)
    {
        border.Background = Brush(_appearance.Background);
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = new Thickness(1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius));
    }

    public void ApplyMediaFrame(Border border)
    {
        border.Background = UsesLumiStyle
            ? Brush(_appearance.CodeBackground)
            : Brush(_appearance.SurfaceAlternate);
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = new Thickness(1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(16D, _appearance.CornerRadius));
        border.ClipToBounds = true;
    }

    public void ApplyStatusPanel(Border border, String tone)
    {
        String accent = tone switch
        {
            "success" or "available" => _brand.Accent,
            "warning" => _brand.Highlight,
            "error" or "critical" => _brand.AccentSecondary,
            _ => _brand.AccentTertiary
        };
        border.Background = Brush(_appearance.Surface);
        border.BorderBrush = Brush(accent);
        border.BorderThickness = new Thickness(5D, 1D, 1D, 1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius));
        border.Padding = new Thickness(UsesLumiStyle ? 20D : 16D, 14D);
    }

    public void ApplyChoiceCard(Border border, Boolean selected)
    {
        border.Background = selected
            ? Brush(_appearance.SurfaceAlternate)
            : Brush(_appearance.Surface);
        border.BorderBrush = Brush(
            selected ? _appearance.Accent : _appearance.Border);
        border.BorderThickness = UsesLumiStyle && selected
            ? new Thickness(5D, 1D, 1D, 1D)
            : new Thickness(selected ? 2D : 1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius));
        border.Padding = new Thickness(16D, 13D);
    }

    public void ApplyChoiceButton(Button button, Boolean selected)
    {
        button.Background = selected
            ? Brush(_appearance.SurfaceAlternate)
            : Brush(_appearance.Surface);
        button.Foreground = Brush(_appearance.Text);
        button.BorderBrush = Brush(
            selected ? _appearance.Accent : _appearance.Border);
        button.BorderThickness = UsesLumiStyle && selected
            ? new Thickness(5D, 1D, 1D, 1D)
            : new Thickness(selected ? 2D : 1D);
        button.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius));
        button.Padding = new Thickness(16D);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public void ApplyDataFrame(Border border)
    {
        border.Background = Brush(_appearance.Surface);
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = new Thickness(1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(14D, _appearance.ControlRadius));
        border.Padding = new Thickness(0D);
    }

    public void ApplyCollectionRow(Border border, Boolean selected = false)
    {
        border.Background = selected
            ? Brush(_appearance.SurfaceAlternate)
            : Brush(_appearance.Surface);
        border.BorderBrush = Brush(
            selected ? _appearance.Accent : _appearance.Border);
        border.BorderThickness = UsesLumiStyle
            ? selected
                ? new Thickness(5D, 0D, 0D, 1D)
                : new Thickness(0D, 0D, 0D, 1D)
            : new Thickness(selected ? 2D : 1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(12D, _appearance.ControlRadius));
        border.Padding = new Thickness(16D, 13D);
    }

    public void ApplySegmentFrame(Border border)
    {
        border.Background = UsesLumiStyle
            ? Brush(_appearance.Surface)
            : Brush(_appearance.SurfaceAlternate);
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = new Thickness(1D);
        border.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(12D, _appearance.ControlRadius));
        border.Padding = new Thickness(4D);
    }

    public void ApplySectionSurface(Border border, String role)
    {
        border.Background = UsesLumiStyle
            ? Brush(role is LumuiProtocol.RegionRoles.Features
                or LumuiProtocol.RegionRoles.Comparison
                    ? _appearance.SurfaceAlternate
                    : _appearance.Surface)
            : UsesDarkSurfaces
            ? role switch
            {
                LumuiProtocol.RegionRoles.Problem =>
                    Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
                LumuiProtocol.RegionRoles.Comparison =>
                    Gradient(_appearance.SurfaceAlternate, _appearance.Surface),
                LumuiProtocol.RegionRoles.Features =>
                    Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
                LumuiProtocol.RegionRoles.CallToAction =>
                    Gradient(_appearance.SurfaceAlternate, _appearance.Surface),
                _ => Brush(_appearance.Surface)
            }
            : role switch
        {
            LumuiProtocol.RegionRoles.Problem =>
                Gradient(_appearance.Surface, _brand.Warm),
            LumuiProtocol.RegionRoles.Comparison =>
                Gradient(_appearance.Surface, _brand.Cool),
            LumuiProtocol.RegionRoles.Features =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            LumuiProtocol.RegionRoles.CallToAction =>
                Gradient(_brand.Warm, _brand.Cool),
            _ => Brush(_appearance.Surface)
        };
        border.BorderBrush = Brush(_appearance.Border);
        border.BorderThickness = new Thickness(0D, 0D, 0D, 1D);
    }

    public void ApplyPrimaryButton(Button button, Boolean selected = false)
    {
        NormalizeButtonContent(button);
        button.Background = PrimaryBrush();
        button.Foreground = Brush(_appearance.AccentText);
        button.BorderBrush = selected
            ? Brush(_appearance.Text)
            : Brush(_appearance.Accent);
        button.BorderThickness = UsesLumiStyle
            ? new Thickness(0D)
            : _appearance.Kind switch
        {
            AppearanceKind.Classic => new Thickness(2D),
            AppearanceKind.Steampunk => new Thickness(2D),
            _ => new Thickness(1D)
        };
        button.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : _appearance.ControlRadius);
        button.Padding = ControlPadding();
        button.MinHeight = ControlHeight();
        button.FontWeight = FontWeight.SemiBold;
    }

    public void ApplyLinkButton(Button button)
    {
        NormalizeButtonContent(button);
        button.Background = UsesLumiStyle
            ? Brushes.Transparent
            : SecondaryControlBrush();
        button.Foreground = Brush(
            UsesLumiStyle ? _appearance.Accent : _appearance.Text);
        button.BorderBrush = Brush(_appearance.Border);
        button.BorderThickness = UsesLumiStyle
            ? new Thickness(2D)
            : _appearance.Kind switch
        {
            AppearanceKind.Classic => new Thickness(2D),
            AppearanceKind.Steampunk => new Thickness(2D),
            _ => new Thickness(1D)
        };
        button.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : _appearance.ControlRadius);
        button.Padding = ControlPadding();
        button.MinHeight = ControlHeight();
        button.FontWeight = FontWeight.SemiBold;
    }

    public void ApplyNavigationButton(Button button, Boolean selected)
    {
        NormalizeButtonContent(button);
        button.Background = selected && !UsesLumiStyle
            ? Brush(_appearance.SurfaceAlternate)
            : Brushes.Transparent;
        button.Foreground = Brush(selected
            ? _appearance.Text
            : _appearance.Muted);
        button.BorderBrush = Brush(selected
            ? _appearance.Accent
            : _appearance.Border);
        button.BorderThickness = UsesLumiStyle
            ? new Thickness(0D, 0D, 0D, selected ? 4D : 1D)
            : new Thickness(0D);
        button.CornerRadius = new CornerRadius(
            UsesLumiStyle ? 0D : Math.Max(10D, _appearance.ControlRadius));
        button.Padding = new Thickness(UsesLumiStyle ? 16D : 14D, 9D);
        button.MinHeight = UsesLumiStyle ? 44D : 38D;
        button.FontWeight = FontWeight.SemiBold;
    }

    public void ApplyTileAccent(Control control, Int32 position)
    {
        String color = (position % 4) switch
        {
            1 => _brand.Accent,
            2 => _brand.AccentSecondary,
            3 => _brand.AccentTertiary,
            _ => _brand.Highlight
        };
        Thickness thickness = UsesLumiStyle
            ? new Thickness(6D, 0D, 0D, 0D)
            : _appearance.Kind switch
        {
            AppearanceKind.Metro => new Thickness(5D, 0D, 0D, 0D),
            AppearanceKind.Classic => new Thickness(2D),
            AppearanceKind.Steampunk => new Thickness(3D),
            AppearanceKind.ScienceFiction =>
                new Thickness(1D, 1D, 4D, 1D),
            _ => new Thickness(1D, 5D, 1D, 1D)
        };
        if (control is Border border)
        {
            border.BorderBrush = Brush(color);
            border.BorderThickness = thickness;
        }
        else if (control is Button button)
        {
            button.BorderBrush = Brush(color);
            button.BorderThickness = thickness;
        }
    }

    private IBrush HeroBrush(DeviceProfileKind profile)
    {
        if (UsesDarkSurfaces)
        {
            return Gradient(
                _appearance.SurfaceAlternate,
                _appearance.Background);
        }
        if (profile == DeviceProfileKind.Desktop)
        {
            return Gradient(_brand.Cool, _appearance.Surface);
        }
        return _appearance.Kind switch
        {
            AppearanceKind.Aero => Gradient(_brand.Cool, _appearance.Surface),
            AppearanceKind.Aqua => Gradient(_brand.Cool, _brand.Warm),
            AppearanceKind.Classic => Gradient(_appearance.SurfaceAlternate, _appearance.Surface),
            AppearanceKind.Steampunk => Gradient(_appearance.SurfaceAlternate, _brand.Warm),
            AppearanceKind.ScienceFiction => Gradient(_appearance.SurfaceAlternate, _appearance.Background),
            _ => Gradient(_brand.Warm, _brand.Cool)
        };
    }

    private IBrush CardBrush(String role)
    {
        if (UsesDarkSurfaces)
        {
            return role == LumuiProtocol.RegionRoles.CallToAction
                || role == LumuiProtocol.RegionRoles.Problem
                    ? Gradient(
                        _appearance.Surface,
                        _appearance.SurfaceAlternate)
                    : Brush(_appearance.Surface);
        }
        if (role == LumuiProtocol.RegionRoles.CallToAction)
        {
            return Gradient(_brand.Warm, _brand.Cool);
        }
        if (role == LumuiProtocol.RegionRoles.Problem)
        {
            return Gradient(_appearance.Surface, _brand.Warm);
        }
        return _appearance.Kind switch
        {
            AppearanceKind.Aero or AppearanceKind.Aqua =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            AppearanceKind.Steampunk =>
                Gradient(_appearance.Surface, _brand.Warm),
            _ => Brush(_appearance.Surface)
        };
    }

    private IBrush BorderBrush(String role)
    {
        if (role == LumuiProtocol.RegionRoles.Problem)
        {
            return Brush(_brand.AccentSecondary);
        }
        if (role == LumuiProtocol.RegionRoles.CallToAction)
        {
            return Brush(_brand.Accent);
        }
        return Brush(_appearance.Border);
    }

    private IBrush PrimaryBrush()
    {
        return _appearance.Kind switch
        {
            AppearanceKind.Aero =>
                Gradient(_appearance.Accent, Darken(_appearance.Accent)),
            AppearanceKind.Aqua =>
                Gradient(Lighten(_appearance.Accent), _appearance.Accent),
            AppearanceKind.Classic =>
                Gradient(_appearance.Accent, Darken(_appearance.Accent)),
            AppearanceKind.Steampunk =>
                Gradient(Lighten(_appearance.Accent), _appearance.Accent),
            _ => Brush(_appearance.Accent)
        };
    }

    private IBrush SecondaryControlBrush()
    {
        return _appearance.Kind switch
        {
            AppearanceKind.Aero or AppearanceKind.Aqua =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            AppearanceKind.Classic =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            AppearanceKind.Steampunk =>
                Gradient(_appearance.Surface, _appearance.SurfaceAlternate),
            _ => Brush(_appearance.Surface)
        };
    }

    private Thickness CardBorderThickness()
    {
        return _appearance.Kind switch
        {
            AppearanceKind.Metro => new Thickness(5D, 0D, 0D, 0D),
            AppearanceKind.Classic => new Thickness(2D),
            AppearanceKind.Steampunk => new Thickness(3D),
            AppearanceKind.ScienceFiction => new Thickness(1D, 1D, 4D, 1D),
            _ => new Thickness(1D)
        };
    }

    private Thickness CardPadding()
    {
        if (UsesLumiStyle)
        {
            return new Thickness(24D, 22D);
        }
        return _appearance.Kind switch
        {
            AppearanceKind.Metro => new Thickness(24D, 22D),
            AppearanceKind.Classic => new Thickness(18D),
            _ => new Thickness(22D)
        };
    }

    private Thickness ControlPadding()
    {
        if (UsesLumiStyle)
        {
            return new Thickness(18D, 11D);
        }
        return _appearance.Kind switch
        {
            AppearanceKind.Metro => new Thickness(18D, 10D),
            AppearanceKind.Classic => new Thickness(14D, 7D),
            _ => new Thickness(18D, 10D)
        };
    }

    private Double ControlHeight()
    {
        return _appearance.Kind switch
        {
            AppearanceKind.Classic => 36D,
            AppearanceKind.Metro => 44D,
            _ => 44D
        };
    }

    private Boolean UsesLumiStyle =>
        _appearance.Kind is AppearanceKind.Material or AppearanceKind.Metro;

    private void AddOrbs(Grid root, DeviceProfileKind profile)
    {
        if (profile == DeviceProfileKind.Desktop)
        {
            root.Children.Add(Orb(
                128D,
                _brand.AccentTertiary,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0D, 28D, 44D, 0D),
                0.42D));
            root.Children.Add(Orb(
                160D,
                _brand.AccentSecondary,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0D, 88D, -28D, 0D),
                0.32D));
            return;
        }
        Double scale = profile switch
        {
            DeviceProfileKind.Watch => 0.38D,
            DeviceProfileKind.Phone => 0.55D,
            DeviceProfileKind.Tablet => 0.8D,
            _ => 1D
        };
        Boolean wide = profile is DeviceProfileKind.Desktop
            or DeviceProfileKind.Web
            or DeviceProfileKind.Kiosk;
        if (wide)
        {
            root.Children.Add(Orb(
                112D,
                _brand.Highlight,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0D, 64D, 430D, 0D)));
            root.Children.Add(Orb(
                210D,
                _brand.AccentSecondary,
                HorizontalAlignment.Right,
                VerticalAlignment.Top,
                new Thickness(0D, 126D, 216D, 0D)));
            root.Children.Add(Orb(
                290D,
                _brand.AccentTertiary,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0D, 30D, 500D, 0D)));
            root.Children.Add(Orb(
                138D,
                _brand.Accent,
                HorizontalAlignment.Right,
                VerticalAlignment.Center,
                new Thickness(0D, 170D, 330D, 0D)));
        }
        root.Children.Add(Orb(
            150D * scale,
            _brand.Highlight,
            HorizontalAlignment.Right,
            VerticalAlignment.Top,
            new Thickness(0D, -32D * scale, 42D * scale, 0D)));
        root.Children.Add(Orb(
            230D * scale,
            _brand.AccentTertiary,
            HorizontalAlignment.Right,
            VerticalAlignment.Bottom,
            new Thickness(0D, 0D, 108D * scale, -72D * scale)));
        root.Children.Add(Orb(
            110D * scale,
            _brand.AccentSecondary,
            HorizontalAlignment.Right,
            VerticalAlignment.Bottom,
            new Thickness(0D, 0D, -24D * scale, -12D * scale)));
    }

    private Control Orb(
        Double size,
        String color,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        Thickness margin,
        Double opacity = 0.52D) =>
        new Ellipse
        {
            Width = size,
            Height = size,
            Fill = Brush(color),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Margin = margin,
            IsHitTestVisible = false,
            Opacity = UsesDarkSurfaces ? 0.72D : opacity
        };

    private Boolean UsesDarkSurfaces => String.Equals(
        _appearance.Id,
        "dark",
        StringComparison.Ordinal);

    private static void NormalizeButtonContent(Button button)
    {
        if (button.Content is not String label)
        {
            return;
        }
        TextAlignment alignment =
            button.HorizontalContentAlignment == HorizontalAlignment.Left
                ? TextAlignment.Left
                : TextAlignment.Center;
        button.Content = new TextBlock
        {
            Text = label,
            TextAlignment = alignment,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    private Control MotifLines() =>
        new Border
        {
            BorderBrush = Brush(_brand.Accent),
            BorderThickness = new Thickness(0D, 0D, 0D, 2D),
            Opacity = 0.35D,
            IsHitTestVisible = false
        };

    private static IBrush Gradient(String start, String end)
        => BrowserBrushCache.Gradient(start, end);

    private static String Darken(String value) =>
        Adjust(value, 0.76D);

    private static String Lighten(String value) =>
        Adjust(value, 1.18D);

    private static String Adjust(String value, Double factor)
    {
        Color color = Color.Parse(value);
        Byte red = (Byte)Math.Clamp(color.R * factor, 0D, 255D);
        Byte green = (Byte)Math.Clamp(color.G * factor, 0D, 255D);
        Byte blue = (Byte)Math.Clamp(color.B * factor, 0D, 255D);
        return Color.FromRgb(red, green, blue).ToString();
    }

    private static IBrush Brush(String value) =>
        BrowserBrushCache.Get(value);
}
