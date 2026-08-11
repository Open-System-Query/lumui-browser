namespace Lumui.Client;

public sealed class LumuiProtocolException : Exception
{
    public LumuiProtocolException(String message)
        : base(message)
    {
    }

    public LumuiProtocolException(String message, Exception innerException)
        : base(message, innerException)
    {
    }
}
