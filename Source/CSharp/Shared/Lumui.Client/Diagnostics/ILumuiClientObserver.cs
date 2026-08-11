namespace Lumui.Client;

public interface ILumuiClientObserver
{
    void Record(LumuiRequestTrace trace);
}
