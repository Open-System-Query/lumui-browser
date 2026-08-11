using Avalonia.Media;

namespace Lumui.Browser.Rendering;

public sealed record NativeMediaPalette(
    IBrush Surface,
    IBrush SurfaceAlternate,
    IBrush Text,
    IBrush Muted,
    IBrush Accent,
    IBrush OnAccent,
    IBrush Border,
    FontFamily FontFamily);
