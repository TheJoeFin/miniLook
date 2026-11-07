using System.Diagnostics;
using System.Text.Json;

using miniLook.Contracts.Services;
using miniLook.Core.Helpers;
using miniLook.Models;

using Windows.Storage;

namespace miniLook.Services;

internal sealed class CalendarCacheService : ICalendarCacheService
{
    private const string CalendarCacheFileName = "CalendarCacheJson.json";

    private readonly SemaphoreSlim _accessGate = new(1, 1);

    private static string CachePath =>
        Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, CalendarCacheFileName);

    public async Task<IReadOnlyList<CalendarEventItem>> GetEventsAsync(
        CancellationToken cancellationToken = default)
    {
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            await using FileStream stream = new(
                CachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync(
                    stream,
                    CalendarCacheJsonContext.Default.ListCalendarEventItem,
                    cancellationToken)
                ?? throw new JsonException("The calendar cache contains no collection.");
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[Cache] The calendar cache is invalid and will be refreshed: {ex.Message}");
            return [];
        }
        finally
        {
            _accessGate.Release();
        }
    }

    public async Task SaveEventsAsync(
        IEnumerable<CalendarEventItem> events,
        CancellationToken cancellationToken = default)
    {
        List<CalendarEventItem> snapshot = events.ToList();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAsync(
                CachePath,
                (stream, token) => JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    CalendarCacheJsonContext.Default.ListCalendarEventItem,
                    token),
                cancellationToken);
        }
        finally
        {
            _accessGate.Release();
        }
    }

    public async Task ClearEventsAsync(CancellationToken cancellationToken = default)
    {
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            File.Delete(CachePath);
        }
        finally
        {
            _accessGate.Release();
        }
    }
}
