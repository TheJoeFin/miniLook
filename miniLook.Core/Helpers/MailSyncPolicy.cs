#nullable enable
using System.Globalization;

namespace miniLook.Core.Helpers;

public static class MailSyncPolicy
{
    /// <summary>Graph's default delta page is small; a larger page keeps a first sync to far fewer requests.</summary>
    public const int DeltaPageSize = 50;

    /// <summary>Graph returns at most this many messages for a filtered message delta.</summary>
    public const int ServerEnumerationLimit = 5000;

    /// <summary>Attachment listings are one request per message, so each refresh fills in only a few.</summary>
    public const int AttachmentBatchSize = 25;

    public static DateTimeOffset GetCutoff(DateTimeOffset now, int months) =>
        now.ToUniversalTime().AddMonths(-Math.Max(1, months));

    public static string ReceivedOnOrAfter(DateTimeOffset cutoff) => $"receivedDateTime ge {FormatUtc(cutoff)}";

    public static string ReceivedBefore(DateTimeOffset cutoff) => $"receivedDateTime lt {FormatUtc(cutoff)}";

    /// <summary>
    /// Whether a failed request for a Graph-issued delta or next link means that link can no longer be used.
    /// The link is opaque and the app adds nothing to it, so a client error can only be about the link itself.
    /// Graph documents only "a 40X-series error with error codes such as syncStateNotFound", hence the broad match.
    /// </summary>
    public static bool IsCursorInvalid(int statusCode, string? errorCode)
    {
        if (statusCode is 400 or 404 or 410)
            return true;

        return errorCode is not null
            && (errorCode.Contains("syncState", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("resync", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a failed attachment listing says the service is busy rather than anything about that message.</summary>
    public static bool IsServiceBusy(int statusCode) => statusCode is 429 or 503 or 504;

    // Invariant culture matters: under the current culture ":" becomes the local time separator
    // and the year follows the local calendar, which turns the filter invalid or into a future date.
    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>A saved delta or next link was rejected, so the mailbox has to be enumerated again.</summary>
public sealed class MailSyncCursorExpiredException(Exception innerException)
    : Exception("The saved mail sync cursor is no longer valid.", innerException);

/// <summary>The saved sign-in can no longer be refreshed silently, and the user has to sign in again.</summary>
public sealed class SignInRequiredException(Exception innerException)
    : Exception("Sign-in is required to continue.", innerException);
