using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Lumui.Browser.Configuration;

namespace Lumui.Browser.Presentation;

public static class BrowserWindowAppearance
{
    private static readonly String[] AccentResourceKeys = new String[]
    {
        "SystemAccentColor",
        "SystemAccentColorDark1",
        "SystemAccentColorDark2",
        "SystemAccentColorDark3",
        "SystemAccentColorLight1",
        "SystemAccentColorLight2",
        "SystemAccentColorLight3",
        "LumiAccentBrush",
        "LumiAccentStrongBrush",
        "LumiAccentHoverBrush",
        "LumiAccentLightBrush",
        "LumiAccentSoftBrush",
        "LumiAccentDarkSurfaceBrush",
        "LumiAccentSelectionBrush"
    };

    public static void Apply(
        Window window,
        BrowserPreferences preferences)
    {
        Boolean dark = preferences.ColorScheme == BrowserColorScheme.Dark;
        window.RequestedThemeVariant = preferences.HighContrast || dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        window.FontFamily = new FontFamily(
            FontPreferenceCatalog.Resolve(
                preferences.Font,
                preferences.FontFamily));
        window.FontSize = 14D;
        ApplyAccent(window, preferences.AccentColor, dark);
        SetClass(window, "dark", dark && !preferences.HighContrast);
        SetClass(window, "high-contrast", preferences.HighContrast);
        SetClass(window, "senior", preferences.SeniorMode);
        SetClass(window, "bionic", preferences.BionicReading);
    }

    public static void Inherit(Window owner, Window window)
    {
        window.RequestedThemeVariant = owner.RequestedThemeVariant;
        window.FontFamily = owner.FontFamily;
        window.FontSize = owner.FontSize;
        foreach (String key in AccentResourceKeys)
        {
            window.Resources[key] = owner.Resources[key];
        }
        foreach (String name in new String[]
        {
            "dark",
            "high-contrast",
            "senior",
            "bionic"
        })
        {
            SetClass(window, name, owner.Classes.Contains(name));
        }
    }

    private static void SetClass(
        Window window,
        String name,
        Boolean enabled)
    {
        if (enabled && !window.Classes.Contains(name))
        {
            window.Classes.Add(name);
        }
        else if (!enabled)
        {
            window.Classes.Remove(name);
        }
    }

    private static void ApplyAccent(
        Window window,
        String accentText,
        Boolean dark)
    {
        Color accent;
        try
        {
            accent = Color.Parse(accentText);
        }
        catch (FormatException)
        {
            accent = Color.Parse(BrowserPreferences.DefaultAccentColor);
        }

        Color dark1 = Mix(accent, Colors.Black, 0.12D);
        Color dark2 = Mix(accent, Colors.Black, 0.25D);
        Color dark3 = Mix(accent, Colors.Black, 0.43D);
        Color light1 = Mix(accent, Colors.White, 0.18D);
        Color light2 = Mix(accent, Colors.White, 0.38D);
        Color light3 = Mix(accent, Colors.White, 0.64D);

        window.Resources["SystemAccentColor"] = accent;
        window.Resources["SystemAccentColorDark1"] = dark1;
        window.Resources["SystemAccentColorDark2"] = dark2;
        window.Resources["SystemAccentColorDark3"] = dark3;
        window.Resources["SystemAccentColorLight1"] = light1;
        window.Resources["SystemAccentColorLight2"] = light2;
        window.Resources["SystemAccentColorLight3"] = light3;
        window.Resources["LumiAccentBrush"] = new SolidColorBrush(accent);
        window.Resources["LumiAccentStrongBrush"] = new SolidColorBrush(dark2);
        window.Resources["LumiAccentHoverBrush"] = new SolidColorBrush(light1);
        window.Resources["LumiAccentLightBrush"] = new SolidColorBrush(light2);
        window.Resources["LumiAccentSoftBrush"] = new SolidColorBrush(
            Mix(accent, dark ? Colors.Black : Colors.White, dark ? 0.62D : 0.83D));
        window.Resources["LumiAccentDarkSurfaceBrush"] = new SolidColorBrush(
            Mix(accent, Colors.Black, 0.68D));
        window.Resources["LumiAccentSelectionBrush"] = new SolidColorBrush(light3);
    }

    private static Color Mix(
        Color source,
        Color target,
        Double targetAmount)
    {
        Byte Blend(Byte from, Byte to) => (Byte)Math.Clamp(
            Math.Round(from + ((to - from) * targetAmount)),
            Byte.MinValue,
            Byte.MaxValue);

        return Color.FromArgb(
            source.A,
            Blend(source.R, target.R),
            Blend(source.G, target.G),
            Blend(source.B, target.B));
    }

}
