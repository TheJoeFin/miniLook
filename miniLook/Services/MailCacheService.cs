using miniLook.Contracts.Services;
using miniLook.Models;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage;

namespace miniLook.Services;
internal class MailCacheService : IMailCacheService
{
    private const string MailCacheFileName = "MailCacheJson.json";

    private const string DeltaLinkKey = "MailCacheDeltaLink";

    private const string MailWindowMonthsKey = "MailWindowMonths";

    private const int DefaultMailWindowMonths = 2;

    private readonly ILocalSettingsService _localSettingsService;

    public MailCacheService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public string? DeltaLink { get; set; }

    public int MailWindowMonths { get; set; } = DefaultMailWindowMonths;

    public async Task ClearMailCacheAsync()
    {
        StorageFolder localCacheFolder = ApplicationData.Current.LocalCacheFolder;
        StorageFile cacheFile = await localCacheFolder.GetFileAsync(MailCacheFileName);
        await cacheFile.DeleteAsync();
    }

    public async Task<IEnumerable<MailData>> GetEmailsAsync()
    {
        StorageFolder localCacheFolder = ApplicationData.Current.LocalCacheFolder;
        StorageFile cacheFile;

        try
        {
            cacheFile = await localCacheFolder.GetFileAsync(MailCacheFileName);
        }
        catch (FileNotFoundException)
        {
            return [];
        }

        string rawJson = string.Empty;
        try
        {
            using TextReader textReader = new StreamReader(await cacheFile.OpenStreamForReadAsync());
            rawJson = textReader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            throw;
        }

        if (string.IsNullOrEmpty(rawJson))
            return [];

        IEnumerable<MailData>? mailData = JsonSerializer.Deserialize<IEnumerable<MailData>>(rawJson);
        
        if (mailData is null)
            return [];

        return mailData;
    }

    public async Task InitializeAsync()
    {
        DeltaLink = await _localSettingsService.ReadSettingAsync<string?>(DeltaLinkKey);
        int? savedMonths = await _localSettingsService.ReadSettingAsync<int?>(MailWindowMonthsKey);
        MailWindowMonths = savedMonths ?? DefaultMailWindowMonths;
    }

    public async Task SaveDeltaLink(string? deltaLink)
    {
        DeltaLink = deltaLink;
        await _localSettingsService.SaveSettingAsync(DeltaLinkKey, deltaLink);
    }

    public async Task SaveMailWindowMonthsAsync(int months)
    {
        MailWindowMonths = months;
        await _localSettingsService.SaveSettingAsync(MailWindowMonthsKey, months);
    }

    public async Task SaveEmailsAsync(IEnumerable<MailData> allMailData)
    {
        string json = JsonSerializer.Serialize(allMailData);

        StorageFolder localCacheFolder = ApplicationData.Current.LocalCacheFolder;
        try
        {
            StorageFile cacheFile = await localCacheFolder.CreateFileAsync(MailCacheFileName, CreationCollisionOption.ReplaceExisting);

            using TextWriter textWriter = new StreamWriter(await cacheFile.OpenStreamForWriteAsync());
            textWriter.Write(json);
        }
        catch (IOException)
        {
        }
        // TODO: Handle exceptions properly, maybe with retry logic
    }
}
