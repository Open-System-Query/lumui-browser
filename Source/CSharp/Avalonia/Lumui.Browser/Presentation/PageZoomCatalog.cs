namespace Lumui.Browser.Presentation;

public static class PageZoomCatalog
{
    private static readonly Int32[] Levels =
    {
        25,
        33,
        50,
        67,
        75,
        80,
        90,
        100,
        110,
        125,
        133,
        150,
        175,
        200,
        250,
        300,
        400,
        500
    };

    public static Int32 Normalize(Int32 value) => Math.Clamp(value, Levels[0], Levels[^1]);

    public static Int32 Next(Int32 current, Int32 direction)
    {
        current = Normalize(current);
        if (direction > 0)
        {
            foreach (Int32 level in Levels)
            {
                if (level > current)
                {
                    return level;
                }
            }
            return Levels[^1];
        }
        if (direction < 0)
        {
            for (Int32 index = Levels.Length - 1; index >= 0; index--)
            {
                if (Levels[index] < current)
                {
                    return Levels[index];
                }
            }
            return Levels[0];
        }
        return current;
    }
}
