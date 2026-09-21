using Microsoft.Extensions.Options;

namespace JobHunter.Worker;

public sealed class QuietHoursWindow
{
    private readonly bool _enabled;
    private readonly TimeOnly _startLocalTime;
    private readonly TimeOnly _endLocalTime;
    private readonly TimeZoneInfo _timeZone;

    public QuietHoursWindow(IOptions<WorkerOptions> options)
        : this(options.Value.QuietHours)
    {
    }

    internal QuietHoursWindow(WorkerQuietHoursOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _enabled = options.Enabled;
        _startLocalTime = options.StartLocalTime;
        _endLocalTime = options.EndLocalTime;
        _timeZone = ResolveTimeZone(options.TimeZoneId);
    }

    public bool IsQuiet(DateTimeOffset utcNow)
    {
        if (!_enabled)
        {
            return false;
        }

        var localTime = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(utcNow, _timeZone).DateTime);

        return _startLocalTime < _endLocalTime
            ? localTime >= _startLocalTime && localTime < _endLocalTime
            : localTime >= _startLocalTime || localTime < _endLocalTime;
    }

    internal static bool HasValidTimeZoneId(string? timeZoneId)
    {
        try
        {
            _ = ResolveTimeZone(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId)
            ? TimeZoneInfo.Local
            : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
}
