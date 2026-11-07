namespace miniLook.Models;

public sealed class CalendarEventDayGroup
{
    public CalendarEventDayGroup(
        DateTime date,
        IEnumerable<CalendarEventItem> items,
        DateTime today)
    {
        Date = date;
        Items = items.ToList();
        Label = GetLabel(date, today);
    }

    public DateTime Date { get; }

    public string Label { get; }

    public IReadOnlyList<CalendarEventItem> Items { get; }

    private static string GetLabel(DateTime date, DateTime today)
    {
        string dayName = date.Date switch
        {
            var value when value == today.Date => "Today",
            var value when value == today.Date.AddDays(1) => "Tomorrow",
            var value when value == today.Date.AddDays(-1) => "Yesterday",
            _ => date.ToString("dddd"),
        };

        return $"{dayName}, {date:MMMM d}";
    }
}
