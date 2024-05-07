using miniLook.Models;

namespace miniLook.Contracts.Services;
public interface IMailCacheService
{
    Task InitializeAsync();

    string? DeltaLink { get; set; }

    Task SaveDeltaLink(string? deltaLink);

    Task<IEnumerable<MailData>> GetEmailsAsync();

    Task SaveEmailsAsync(IEnumerable<MailData> allMailData);

    Task ClearMailCacheAsync();
}
