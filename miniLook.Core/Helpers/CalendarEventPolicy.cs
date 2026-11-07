namespace miniLook.Core.Helpers;

public static class CalendarEventPolicy
{
    public static DateTime GetDefaultStart(DateTime now)
    {
        DateTime rounded = new(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute >= 30 ? 30 : 0,
            0,
            now.Kind);

        return rounded.AddMinutes(rounded <= now ? 30 : 0);
    }

    public static bool TryGetRange(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        TimeSpan? startTime,
        TimeSpan? endTime,
        bool isAllDay,
        out DateTime start,
        out DateTime end,
        out string error)
    {
        if (!isAllDay && (!startTime.HasValue || !endTime.HasValue))
        {
            start = default;
            end = default;
            error = "Choose a start and end time.";
            return false;
        }

        TimeSpan effectiveStartTime = isAllDay ? TimeSpan.Zero : startTime!.Value;
        TimeSpan effectiveEndTime = isAllDay ? TimeSpan.Zero : endTime!.Value;

        start = Combine(startDate, effectiveStartTime);
        end = Combine(endDate, effectiveEndTime);
        error = string.Empty;

        if (startTime.HasValue && !IsValidTime(startTime.Value))
        {
            error = "Choose a valid start time.";
            return false;
        }

        if (endTime.HasValue && !IsValidTime(endTime.Value))
        {
            error = "Choose a valid end time.";
            return false;
        }

        if (end <= start)
        {
            error = "The end must be after the start.";
            return false;
        }

        return true;
    }

    private static DateTime Combine(DateTimeOffset date, TimeSpan time) =>
        new(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds, DateTimeKind.Unspecified);

    private static bool IsValidTime(TimeSpan value) =>
        value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
}
