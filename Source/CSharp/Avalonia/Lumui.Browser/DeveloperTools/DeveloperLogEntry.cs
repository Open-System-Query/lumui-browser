namespace Lumui.Browser.DeveloperTools;

public sealed class DeveloperLogEntry
{
    public DeveloperLogEntry(
        DateTimeOffset timestamp,
        DeveloperLogLevel level,
        String message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public DeveloperLogLevel Level { get; }

    public String Message { get; }

    public override String ToString() =>
        $"{Timestamp:HH:mm:ss.fff}  {Level.ToString().ToUpperInvariant(),-11}  {Message}";
}
