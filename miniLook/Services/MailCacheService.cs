using miniLook.Contracts.Services;
using miniLook.Models;
using System.Text.Json;
using Windows.Storage;

namespace miniLook.Services;
internal class MailCacheService : IMailCacheService
{
    private const string MailCacheFileName = "MailCacheJson.json";

    private const string DeltaLinkKey = "MailCacheDeltaLink";

    private readonly ILocalSettingsService _localSettingsService;

    public MailCacheService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public string? DeltaLink { get; set; }

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

        using TextReader textReader = new StreamReader(await cacheFile.OpenStreamForReadAsync());
        string? rawJson = textReader.ReadToEnd();

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
    }

    public async Task SaveDeltaLink(string? deltaLink)
    {
        DeltaLink = deltaLink;
        await _localSettingsService.SaveSettingAsync(DeltaLinkKey, deltaLink);
    }

    public async Task SaveEmailsAsync(IEnumerable<MailData> allMailData)
    {
        string json = JsonSerializer.Serialize(allMailData);

        StorageFolder localCacheFolder = ApplicationData.Current.LocalCacheFolder;
        StorageFile cacheFile = await localCacheFolder.CreateFileAsync(MailCacheFileName, CreationCollisionOption.ReplaceExisting);

        using TextWriter textWriter = new StreamWriter(await cacheFile.OpenStreamForWriteAsync());
        textWriter.Write(json);
    }
}
