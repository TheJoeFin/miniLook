using miniLook.Core.Helpers;

namespace miniLook.Core.Tests;

public class CalendarEventPolicyTests
{
    [Theory]
    [InlineData(9, 0, 9, 30)]
    [InlineData(9, 15, 9, 30)]
    [InlineData(9, 30, 10, 0)]
    [InlineData(23, 45, 0, 0)]
    public void DefaultStartRoundsForwardToTheNextHalfHour(int hour, int minute, int expectedHour, int expectedMinute)
    {
        DateTime now = new(2026, 11, 7, hour, minute, 17);

        DateTime result = CalendarEventPolicy.GetDefaultStart(now);

        DateTime expected = new(2026, 11, 7, expectedHour, expectedMinute, 0);
        if (hour == 23)
            expected = expected.AddDays(1);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TimedEventsRequireTheEndToFollowTheStart()
    {
        DateTimeOffset date = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

        bool valid = CalendarEventPolicy.TryGetRange(
            date,
            date,
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(9),
            false,
            out _,
            out _,
            out string error);

        Assert.False(valid);
        Assert.Equal("The end must be after the start.", error);
    }

    [Fact]
    public void TimedEventsRequireBothTimes()
    {
        DateTimeOffset date = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

        bool valid = CalendarEventPolicy.TryGetRange(
            date,
            date,
            null,
            TimeSpan.FromHours(10),
            false,
            out _,
            out _,
            out string error);

        Assert.False(valid);
        Assert.Equal("Choose a start and end time.", error);
    }

    [Fact]
    public void AllDayEventsUseMidnightAndAnExclusiveEndDate()
    {
        DateTimeOffset startDate = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset endDate = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

        bool valid = CalendarEventPolicy.TryGetRange(
            startDate,
            endDate,
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(11),
            true,
            out DateTime start,
            out DateTime end,
            out string error);

        Assert.True(valid);
        Assert.Empty(error);
        Assert.Equal(new DateTime(2026, 11, 7, 0, 0, 0), start);
        Assert.Equal(new DateTime(2026, 11, 8, 0, 0, 0), end);
    }

    [Fact]
    public void ExplicitOnlineMeetingUrlIsPreferred()
    {
        string? link = CalendarEventPolicy.GetMeetingLink(
            "https://meet.example.test/room/123",
            "<a href=\"https://zoom.us/j/123\">Join Zoom</a>",
            null,
            null);

        Assert.Equal("https://meet.example.test/room/123", link);
    }

    [Theory]
    [InlineData("<a href=\"https://teams.microsoft.com/l/meetup-join/abc\">Join Teams</a>")]
    [InlineData("Dial in at https://contoso.zoom.us/j/123456?pwd=abc.")]
    public void RecognizedMeetingLinksAreFoundInEventDetails(string content)
    {
        string? link = CalendarEventPolicy.GetMeetingLink(null, content, null, null);

        Assert.NotNull(link);
    }

    [Fact]
    public void UnrelatedEventLinksAreIgnored()
    {
        string? link = CalendarEventPolicy.GetMeetingLink(
            null,
            "<a href=\"https://outlook.office.com/calendar/item/123\">Open event</a>",
            null,
            "https://example.com/agenda");

        Assert.Null(link);
    }
}
