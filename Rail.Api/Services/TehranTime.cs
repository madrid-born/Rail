namespace Rail.Api.Services;

internal static class TehranTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static DateTime Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;
}
