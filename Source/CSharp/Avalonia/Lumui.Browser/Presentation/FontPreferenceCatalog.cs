namespace Lumui.Browser.Presentation;

public static class FontPreferenceCatalog
{
    public static String Resolve(
        FontPreference preference,
        String customFontFamily)
    {
        if (!String.IsNullOrWhiteSpace(customFontFamily))
        {
            return customFontFamily.Trim();
        }
        return Resolve(preference);
    }

    public static String Resolve(FontPreference preference) =>
        preference switch
        {
            FontPreference.Accessible => "Segoe UI",
            FontPreference.DyslexiaFriendly => "Verdana",
            FontPreference.Serif => "Georgia",
            FontPreference.Monospace => "Cascadia Mono, Consolas",
            _ => "Segoe UI"
        };
}
