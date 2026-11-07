using miniLook.Contracts.Services;
using miniLook.Models;
using miniLook.Core.Helpers;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage;

namespace miniLook.Services;
internal class MailCacheService : IMailCacheService
{
    private const string MailCacheFileName = "MailCacheJson.json";

    private const string DeltaLinkKey = "MailCacheDeltaLink";

    private const string MailWindowMonthsKey = "MailWindowMonths";

    private const string SyncStateVersionKey = "MailSyncStateVersion";

    // Raise this when a saved cache can no longer be trusted. Dropping the cursor makes the next sync
    // list the whole window and correct every cached message. Version 2 repairs read states lost by
    // a cache format that left IsRead out.
    private const int CurrentSyncStateVersion = 2;

    private const int DefaultMailWindowMonths = 2;

    private readonly ILocalSettingsService _localSettingsService;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private static string CachePath => Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, MailCacheFileName);

    public MailCacheService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public string? DeltaLink { get; set; }

    public int MailWindowMonths { get; set; } = DefaultMailWindowMonths;

    public async Task ClearMailCacheAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                File.Delete(CachePath);
                string attachments = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Attachments");
                if (Directory.Exists(attachments))
                {
                    foreach (string file in Directory.GetFiles(attachments))
                        File.Delete(file);
                }
            });
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IEnumerable<MailData>> GetEmailsAsync()
    {
        try
        {
            return await Task.Run(async () =>
            {
                await using FileStream stream = new(CachePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonSerializer.DeserializeAsync(stream, MailCacheJsonContext.Default.ListMailData)
                    ?? throw new JsonException("The mail cache contains no collection.");
            });
        }
        catch (FileNotFoundException)
        {
            await SaveDeltaLink(null);
            return [];
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[Cache] The mail cache is invalid and will be refreshed: {ex.Message}");
            await SaveDeltaLink(null);
            return [];
        }
    }

    public async Task InitializeAsync()
    {
        DeltaLink = await _localSettingsService.ReadSettingAsync<string?>(DeltaLinkKey);
        int? savedMonths = await _localSettingsService.ReadSettingAsync<int?>(MailWindowMonthsKey);
        MailWindowMonths = savedMonths ?? DefaultMailWindowMonths;

        int? savedVersion = await _localSettingsService.ReadSettingAsync<int?>(SyncStateVersionKey);
        if (savedVersion != CurrentSyncStateVersion)
        {
            await SaveDeltaLink(null);
            await _localSettingsService.SaveSettingAsync(SyncStateVersionKey, CurrentSyncStateVersion);
        }
    }

    public async Task SaveDeltaLink(string? deltaLink)
    {
        await _localSettingsService.SaveSettingAsync(DeltaLinkKey, deltaLink);
        DeltaLink = deltaLink;
    }

    public async Task SaveMailWindowMonthsAsync(int months)
    {
        MailWindowMonths = months;
        await _localSettingsService.SaveSettingAsync(MailWindowMonthsKey, months);
    }

    public async Task SaveEmailsAsync(IEnumerable<MailData> allMailData, CancellationToken cancellationToken = default)
    {
        List<MailData> snapshot = allMailData.Select(mail => mail.CreateCacheSnapshot()).ToList();
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() => AtomicFile.WriteAsync(CachePath,
                (stream, token) => JsonSerializer.SerializeAsync(stream, snapshot, MailCacheJsonContext.Default.ListMailData, token),
                cancellationToken), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}