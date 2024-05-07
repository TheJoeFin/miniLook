using miniLook.Models;

namespace miniLook.Contracts.Services;
public interface IMailCacheService
{
    Task<IEnumerable<MailData>> GetEmailsAsync();

    Task SaveEmailsAsync(IEnumerable<MailData> allMailData);

    Task ClearMailCacheAsync();
}
