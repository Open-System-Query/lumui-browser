namespace Lumui.Client;

internal sealed class NullLumuiClientObserver : ILumuiClientObserver
{
    public static NullLumuiClientObserver Instance { get; } =
        new NullLumuiClientObserver();

    private NullLumuiClientObserver()
    {
    }

    public void Record(LumuiRequestTrace trace)
    {
    }
}
