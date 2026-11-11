using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Rail.Api.Models.Metro;

namespace Rail.Api.Services;

public sealed class HolidayCalendar(HttpClient client, IMemoryCache cache)
{
    public async Task<TimeTableServiceDay> GetDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var key = (nameof(HolidayCalendar), date);
        if (cache.TryGetValue<TimeTableServiceDay>(key, out var cached)) return cached!;
        try
        {
            var holiday = false;
            try
            {
                var response = await client.GetFromJsonAsync<HolidayResponse>(
                    $"gregorian/{date.Year}/{date.Month}/{date.Day}", cancellationToken);
                if (response?.IsHoliday == null)
                {
                    throw new JsonException("Missing is_holiday flag.");
                }
                holiday = response.IsHoliday.Value;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            var day = new TimeTableServiceDay(date, date.DayOfWeek.ToString(), holiday,
                holiday ? ServiceDayType.FridayOrHoliday : ServiceDayType.DefaultCodeFor(date));
            cache.Set(key, day, TimeSpan.FromHours(24));
            return day;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new HolidayCalendarException("The holiday service could not verify the requested service date.", exception);
        }
    }

    private sealed record HolidayResponse([property: JsonPropertyName("is_holiday")] bool? IsHoliday);
}

public sealed class HolidayCalendarException(string message, Exception innerException) : Exception(message, innerException);
public sealed record TimeTableServiceDay(DateOnly ServiceDate, string DayOfWeek, bool IsHoliday, string DayTypeCode);
