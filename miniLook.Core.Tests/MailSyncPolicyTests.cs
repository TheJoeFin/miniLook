using System.Globalization;
using miniLook.Core.Helpers;

namespace miniLook.Core.Tests;

public class MailSyncPolicyTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 9, 6, 7, 5, 9, TimeSpan.Zero);

    [Theory]
    [InlineData("en-US")]
    [InlineData("fi-FI")] // Uses "." as the time separator.
    [InlineData("th-TH")] // Uses the Buddhist calendar, so the year would be 2569.
    [InlineData("ar-SA")] // Uses the Hijri calendar.
    public void FiltersAreTheSameUnderEveryCulture(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("receivedDateTime ge 2026-09-06T07:05:09Z", MailSyncPolicy.ReceivedOnOrAfter(Cutoff));
            Assert.Equal("receivedDateTime lt 2026-09-06T07:05:09Z", MailSyncPolicy.ReceivedBefore(Cutoff));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void FiltersAreAlwaysInUtc()
    {
        DateTimeOffset local = new(2026, 9, 6, 9, 5, 9, TimeSpan.FromHours(2));

        Assert.Equal("receivedDateTime ge 2026-09-06T07:05:09Z", MailSyncPolicy.ReceivedOnOrAfter(local));
    }

    [Fact]
    public void TheCutoffGoesBackByCalendarMonths()
    {
        DateTimeOffset now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero), MailSyncPolicy.GetCutoff(now, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ABadSavedWindowStillLooksBackAMonthRatherThanForward(int months)
    {
        DateTimeOffset now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddMonths(-1), MailSyncPolicy.GetCutoff(now, months));
    }

    [Theory]
    [InlineData(410, null)] // Graph's documented synchronization reset.
    [InlineData(410, "resyncRequired")]
    [InlineData(400, "syncStateNotFound")] // Graph's documented token expiry.
    [InlineData(404, "SyncStateNotFound")]
    [InlineData(400, "SyncStateInvalid")]
    [InlineData(400, "ErrorInvalidSyncStateData")]
    [InlineData(400, "someFutureCode")] // Only the link can be wrong: the app adds nothing to it.
    [InlineData(404, null)]
    [InlineData(500, "resyncRequired")]
    public void RejectedLinksAreTreatedAsExpired(int status, string? code) =>
        Assert.True(MailSyncPolicy.IsCursorInvalid(status, code));

    [Theory]
    [InlineData(401, "InvalidAuthenticationToken")]
    [InlineData(403, "ErrorAccessDenied")]
    [InlineData(429, "TooManyRequests")]
    [InlineData(500, "generalException")]
    [InlineData(503, null)]
    [InlineData(504, "UnknownError")]
    public void OutagesAndAuthProblemsDoNotCostTheCursor(int status, string? code) =>
        Assert.False(MailSyncPolicy.IsCursorInvalid(status, code));

    [Theory]
    [InlineData(true, true, VisibilityAction.RefreshNow)]
    [InlineData(false, true, VisibilityAction.ContinueQuietly)] // Closing the flyout must not throw away a running sync.
    [InlineData(false, false, VisibilityAction.Cancel)] // Background updates off: nothing may run while hidden.
    [InlineData(true, false, VisibilityAction.Cancel)] // Signed out.
    public void VisibilityChangesOnlyCancelWhenPollingIsNotAllowed(bool visible, bool canPoll, VisibilityAction expected) =>
        Assert.Equal(expected, PollingPolicy.OnVisibilityChanged(visible, canPoll));
}
