namespace Rail.Api.Services;

internal static class TehranTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static DateTime Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;

    public static DateTimeOffset Local(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), Zone.GetUtcOffset(value));

    public static DateTimeOffset Convert(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone);

    public static bool TryParse(string value, out DateTimeOffset result)
    {
        result = default;
        // Offset-free input is a Tehran wall-clock time, independent of the server's zone.
        if (!System.Text.RegularExpressions.Regex.IsMatch(value,
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100))) return false;
        try
        {
            if (value.EndsWith('Z') || value.EndsWith('z') || value.LastIndexOf('+') > 10 || value.LastIndexOf('-') > 10)
            {
                if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var instant)) return false;
                result = Convert(instant);
            }
            else
            {
                if (!DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var local)
                    || Zone.IsInvalidTime(local) || Zone.IsAmbiguousTime(local)) return false;
                result = Local(local);
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
