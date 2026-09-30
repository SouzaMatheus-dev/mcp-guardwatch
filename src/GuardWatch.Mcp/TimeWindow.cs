using System.Globalization;

namespace GuardWatch.Mcp;

public static class TimeWindow
{
    public static (string Start, string End) Resolve(int minutes, string? start, string? end)
    {
        if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
            return (start.Trim(), end.Trim());

        var finish = DateTimeOffset.UtcNow;
        var begin = finish.AddMinutes(-Math.Clamp(minutes, 1, 10_080));
        return (Format(begin), Format(finish));
    }

    public static (string Start, string End) LastHours(int hours)
    {
        var finish = DateTimeOffset.UtcNow;
        var begin = finish.AddHours(-Math.Clamp(hours, 1, 168));
        return (Format(begin), Format(finish));
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
