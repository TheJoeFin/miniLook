using miniLook.Models;

namespace miniLook.Contracts.Services;

public interface ICalendarCacheService
{
    Task<IReadOnlyList<CalendarEventItem>> GetEventsAsync(CancellationToken cancellationToken = default);

    Task SaveEventsAsync(IEnumerable<CalendarEventItem> events, CancellationToken cancellationToken = default);

    Task ClearEventsAsync(CancellationToken cancellationToken = default);
}
