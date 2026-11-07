using System.Text.Json;

using miniLook.Models;

namespace miniLook.Core.Tests;

public class CalendarCacheRoundTripTests
{
    [Fact]
    public void EventDetailsSurviveTheCacheRoundTrip()
    {
        CalendarEventItem item = new()
        {
            Id = "event-id",
            Subject = "Planning",
            Start = new DateTime(2026, 11, 7, 9, 0, 0),
            End = new DateTime(2026, 11, 7, 10, 30, 0),
            IsAllDay = false,
            Location = "Conference room",
            Body = "Agenda",
            WebLink = "https://outlook.example/event-id",
            MeetingLink = "https://teams.microsoft.com/l/meetup-join/123",
        };

        string json = JsonSerializer.Serialize(
            new List<CalendarEventItem> { item },
            CalendarCacheJsonContext.Default.ListCalendarEventItem);
        CalendarEventItem restored = JsonSerializer.Deserialize(
            json,
            CalendarCacheJsonContext.Default.ListCalendarEventItem)![0];

        Assert.Equal(item.Id, restored.Id);
        Assert.Equal(item.Subject, restored.Subject);
        Assert.Equal(item.Start, restored.Start);
        Assert.Equal(item.End, restored.End);
        Assert.Equal(item.IsAllDay, restored.IsAllDay);
        Assert.Equal(item.Location, restored.Location);
        Assert.Equal(item.Body, restored.Body);
        Assert.Equal(item.WebLink, restored.WebLink);
        Assert.Equal(item.MeetingLink, restored.MeetingLink);
    }

    [Fact]
    public void DisplayOnlyPropertiesAreNotPersisted()
    {
        CalendarEventItem item = new()
        {
            Id = "event-id",
            Subject = "Planning",
            Start = new DateTime(2026, 11, 7, 9, 0, 0),
            End = new DateTime(2026, 11, 7, 10, 30, 0),
        };

        string json = JsonSerializer.Serialize(
            new List<CalendarEventItem> { item },
            CalendarCacheJsonContext.Default.ListCalendarEventItem);

        Assert.DoesNotContain(nameof(CalendarEventItem.DisplayDate), json);
        Assert.DoesNotContain(nameof(CalendarEventItem.DisplayTime), json);
        Assert.DoesNotContain(nameof(CalendarEventItem.HasLocation), json);
        Assert.DoesNotContain(nameof(CalendarEventItem.HasMeetingLink), json);
        Assert.DoesNotContain(nameof(CalendarEventItem.AccessibilityLabel), json);
    }

    [Fact]
    public void UpdatingAnEventRaisesOnlyChangedPropertyNotifications()
    {
        CalendarEventItem item = new()
        {
            Id = "event-id",
            Subject = "Planning",
            Start = new DateTime(2026, 11, 7, 9, 0, 0),
            End = new DateTime(2026, 11, 7, 10, 30, 0),
        };
        List<string?> notifications = [];
        item.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        item.UpdateFrom(new CalendarEventItem
        {
            Id = "event-id",
            Subject = "Planning updated",
            Start = item.Start,
            End = item.End,
        });

        Assert.Contains(nameof(CalendarEventItem.Subject), notifications);
        Assert.Contains(nameof(CalendarEventItem.AccessibilityLabel), notifications);
        Assert.DoesNotContain(nameof(CalendarEventItem.Start), notifications);
        Assert.DoesNotContain(nameof(CalendarEventItem.DisplayTime), notifications);
    }

    [Fact]
    public void UpdatingAnEventWithIdenticalValuesRaisesNoNotifications()
    {
        CalendarEventItem item = new()
        {
            Id = "event-id",
            Subject = "Planning",
            Start = new DateTime(2026, 11, 7, 9, 0, 0),
            End = new DateTime(2026, 11, 7, 10, 30, 0),
        };
        int notifications = 0;
        item.PropertyChanged += (_, _) => notifications++;

        item.UpdateFrom(new CalendarEventItem
        {
            Id = item.Id,
            Subject = item.Subject,
            Start = item.Start,
            End = item.End,
        });

        Assert.Equal(0, notifications);
    }
}
