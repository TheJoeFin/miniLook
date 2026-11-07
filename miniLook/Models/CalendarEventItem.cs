namespace miniLook.Models;

public sealed class CalendarEventItem
{
    public required string Id { get; init; }

    public required string Subject { get; init; }

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    public bool IsAllDay { get; init; }

    public string Location { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string? WebLink { get; init; }

    public string DisplayDate => IsAllDay
        ? $"{Start:ddd, MMM d} - all day"
        : Start.Date == End.Date
            ? $"{Start:ddd, MMM d}"
            : $"{Start:ddd, MMM d} - {End:ddd, MMM d}";

    public string DisplayTime => IsAllDay
        ? "All day"
        : Start.Date == End.Date
            ? $"{Start:t} - {End:t}"
            : $"{Start:t} - {End:ddd, MMM d t}";

    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);

    public string AccessibilityLabel => HasLocation
        ? $"{Subject}, {DisplayDate}, {DisplayTime}, {Location}"
        : $"{Subject}, {DisplayDate}, {DisplayTime}";
}
