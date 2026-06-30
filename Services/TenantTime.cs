using GeneradorTurnos.Models;

namespace GeneradorTurnos.Services;

public static class TenantTime
{
    private static readonly Dictionary<string, string> WindowsFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Bogota"] = "SA Pacific Standard Time",
        ["America/Mexico_City"] = "Central Standard Time (Mexico)",
        ["America/Lima"] = "SA Pacific Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["Europe/Madrid"] = "Romance Standard Time"
    };

    public static DateTime Now(Tenant? tenant)
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Resolve(tenant?.TimeZoneId)).DateTime;

    public static DateTime Today(Tenant? tenant) => Now(tenant).Date;

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? "America/Bogota" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (WindowsFallback.TryGetValue(id, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
        catch (InvalidTimeZoneException) when (WindowsFallback.TryGetValue(id, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }
}
