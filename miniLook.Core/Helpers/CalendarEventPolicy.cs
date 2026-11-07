#nullable enable

using System.Net;
using System.Text.RegularExpressions;

namespace miniLook.Core.Helpers;

public static class CalendarEventPolicy
{
    private static readonly Regex WebUrlPattern = new(
        @"https?://[^\s<""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

    public static string? GetMeetingLink(
        string? onlineMeetingUrl,
        string? body,
        string? bodyPreview,
        string? location)
    {
        string? explicitLink = NormalizeHttpUrl(onlineMeetingUrl);
        if (explicitLink is not null)
            return explicitLink;

        foreach (string? content in new[] { body, bodyPreview, location })
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            string decoded = WebUtility.HtmlDecode(content);
            foreach (Match match in WebUrlPattern.Matches(decoded))
            {
                string candidate = match.Value.TrimEnd('.', ',', ';', '!', ')', ']', '}');
                string? link = NormalizeHttpUrl(candidate);
                if (link is not null && IsKnownMeetingUrl(new Uri(link)))
                    return link;
            }
        }

        return null;
    }

    private static DateTime Combine(DateTimeOffset date, TimeSpan time) =>
        new(date.Year, date.Month, date.Day, time.Hours, time.Minutes, time.Seconds, DateTimeKind.Unspecified);

    private static bool IsValidTime(TimeSpan value) =>
        value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);

    private static string? NormalizeHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static bool IsKnownMeetingUrl(Uri uri)
    {
        string host = uri.IdnHost.TrimEnd('.');
        string path = uri.AbsolutePath;

        if (host is "teams.microsoft.com" or "teams.live.com")
            return path.Contains("/meet", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/l/meetup-join/", StringComparison.OrdinalIgnoreCase);

        if (IsHost(host, "zoom.us")
            || IsHost(host, "zoom.com")
            || IsHost(host, "zoomgov.com"))
        {
            return path.StartsWith("/j/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/wc/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/my/", StringComparison.OrdinalIgnoreCase);
        }

        if (host == "meet.google.com")
            return path.Length > 1;

        if (IsHost(host, "webex.com"))
            return path.Contains("/meet", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/join", StringComparison.OrdinalIgnoreCase);

        return host is "meet.goto.com"
            or "global.gotomeeting.com"
            or "gotomeet.me"
            && path.Length > 1;
    }

    private static bool IsHost(string host, string suffix) =>
        host == suffix || host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase);
}
