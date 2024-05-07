using miniLook.Contracts.Services;
using miniLook.Models;
using System.Text.Json;

namespace miniLook.Services;
internal class MailCacheService : IMailCacheService
{
    private const string SettingsKey = "MailCacheJson";

    private readonly ILocalSettingsService _localSettingsService;

    public MailCacheService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task ClearMailCacheAsync()
    {
        await _localSettingsService.SaveSettingAsync(SettingsKey, string.Empty);
    }

    public async Task<IEnumerable<MailData>> GetEmailsAsync()
    {
        string? rawJson = await _localSettingsService.ReadSettingAsync<string>(SettingsKey);

        if (string.IsNullOrEmpty(rawJson))
            return [];

        IEnumerable<MailData>? mailData = JsonSerializer.Deserialize<IEnumerable<MailData>>(rawJson);
        
        if (mailData is null)
            return [];

        return mailData;
    }

    public async Task SaveEmailsAsync(IEnumerable<MailData> allMailData)
    {
        string json = JsonSerializer.Serialize(allMailData);
        await _localSettingsService.SaveSettingAsync(SettingsKey, json);
    }
}
