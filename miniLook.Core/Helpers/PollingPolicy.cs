namespace miniLook.Core.Helpers;

public enum SyncOutcome
{
    Skipped,
    Succeeded,
    Failed
}

public enum VisibilityAction
{
    /// <summary>Someone is looking: refresh as soon as any running pass finishes.</summary>
    RefreshNow,

    /// <summary>Hidden, but background updates are on: let a running pass finish and slow down afterwards.</summary>
    ContinueQuietly,

    /// <summary>Polling is no longer allowed: stop now. A later pass resumes where this one stopped.</summary>
    Cancel
}

public static class PollingPolicy
{
    public static TimeSpan GetInterval(bool visible, int failures) =>
        TimeSpan.FromSeconds(Math.Min(600, (visible ? 10 : 60) * Math.Pow(2, Math.Clamp(failures, 0, 6))));

    public static TimeSpan GetCalendarInterval(bool visible) =>
        TimeSpan.FromMinutes(visible ? 1 : 5);

    public static VisibilityAction OnVisibilityChanged(bool visible, bool canPoll) =>
        !canPoll ? VisibilityAction.Cancel
        : visible ? VisibilityAction.RefreshNow
        : VisibilityAction.ContinueQuietly;
}
