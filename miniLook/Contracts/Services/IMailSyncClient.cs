using Microsoft.Graph.Models;

namespace miniLook.Contracts.Services;

/// <summary>One page of a delta round. Graph sends either a next link or a delta link, never both.</summary>
public sealed record MailDeltaPage(IReadOnlyList<Message> Messages, string? NextLink, string? DeltaLink);

public interface IMailSyncClient
{
    /// <summary>Starts a round that lists all current inbox mail received on or after <paramref name="cutoff"/>.</summary>
    Task<MailDeltaPage> GetInitialPageAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);

    /// <summary>Follows a saved delta link or next link.</summary>
    /// <exception cref="miniLook.Core.Helpers.MailSyncCursorExpiredException">The link can no longer be used.</exception>
    Task<MailDeltaPage> GetLinkedPageAsync(string link, CancellationToken cancellationToken);

    Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string messageId, CancellationToken cancellationToken);
}
