using miniLook.Models;

namespace miniLook.Contracts.Services;
public interface IMailCacheService
{
    Task InitializeAsync();

    object? DeltaLink { get; }

    Task SaveDeltaLink(object? deltaLink);

    Task<IEnumerable<MailData>> GetEmailsAsync();

    Task SaveEmailsAsync(IEnumerable<MailData> allMailData);

    Task ClearMailCacheAsync();
}
