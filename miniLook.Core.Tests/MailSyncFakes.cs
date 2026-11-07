using System.Collections.ObjectModel;
using Microsoft.Graph.Models;
using miniLook.Contracts.Services;
using miniLook.Models;

namespace miniLook.Core.Tests;

/// <summary>A scripted mailbox. A request with nothing scripted fails the test, so stray traffic is visible.</summary>
internal sealed class FakeMailSyncClient : IMailSyncClient
{
    public const string Initial = "<initial>";

    private readonly Dictionary<string, Queue<Func<MailDeltaPage>>> _pages = [];

    public List<string> Requests { get; } = [];

    public List<DateTimeOffset> Cutoffs { get; } = [];

    public List<string> AttachmentRequests { get; } = [];

    public Func<string, IReadOnlyList<Attachment>> Attachments { get; set; } = _ => [new FileAttachment { Id = "a", Name = "file.pdf" }];

    /// <summary>Scripts the replies for a link, used in order. The last one repeats.</summary>
    public FakeMailSyncClient On(string link, params Func<MailDeltaPage>[] replies)
    {
        _pages[link] = new Queue<Func<MailDeltaPage>>(replies);
        return this;
    }

    public FakeMailSyncClient On(string link, MailDeltaPage page) => On(link, () => page);

    public Task<MailDeltaPage> GetInitialPageAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        Cutoffs.Add(cutoff);
        return Reply(Initial, cancellationToken);
    }

    public Task<MailDeltaPage> GetLinkedPageAsync(string link, CancellationToken cancellationToken) => Reply(link, cancellationToken);

    public Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AttachmentRequests.Add(messageId);
        return Task.FromResult(Attachments(messageId));
    }

    private Task<MailDeltaPage> Reply(string link, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(link);
        if (!_pages.TryGetValue(link, out Queue<Func<MailDeltaPage>>? replies))
            throw new InvalidOperationException($"Nothing scripted for {link}");

        Func<MailDeltaPage> reply = replies.Count > 1 ? replies.Dequeue() : replies.Peek();
        return Task.FromResult(reply());
    }
}

/// <summary>An in-memory cache that records the order of saves and what was last written.</summary>
internal sealed class FakeMailCache : IMailCacheService
{
    public string? DeltaLink { get; set; }

    public int MailWindowMonths { get; set; } = 2;

    public List<string> Writes { get; } = [];

    public List<string> SavedIds { get; private set; } = [];

    public int MailSaves { get; private set; }

    public Exception? NextMailSaveFailure { get; set; }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task SaveDeltaLink(string? deltaLink)
    {
        DeltaLink = deltaLink;
        Writes.Add($"cursor:{deltaLink ?? "null"}");
        return Task.CompletedTask;
    }

    public Task<IEnumerable<MailData>> GetEmailsAsync() => Task.FromResult<IEnumerable<MailData>>([]);

    public Task SaveEmailsAsync(IEnumerable<MailData> allMailData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (NextMailSaveFailure is Exception failure)
        {
            NextMailSaveFailure = null;
            throw failure;
        }

        SavedIds = allMailData.Select(mail => mail.Id).ToList();
        MailSaves++;
        Writes.Add("mail");
        return Task.CompletedTask;
    }

    public Task ClearMailCacheAsync() => Task.CompletedTask;

    public Task SaveMailWindowMonthsAsync(int months)
    {
        MailWindowMonths = months;
        return Task.CompletedTask;
    }
}

internal static class Mail
{
    public static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A complete message, the way Graph sends new mail. Age is days before <see cref="Now"/>.</summary>
    public static Message Full(string id, int ageDays = 1, bool isRead = false, bool hasAttachments = false, string conversation = "conversation") => new()
    {
        Id = id,
        Subject = $"Subject {id}",
        ConversationId = conversation,
        IsRead = isRead,
        HasAttachments = hasAttachments,
        ReceivedDateTime = Now.AddDays(-ageDays),
    };

    /// <summary>A changed message carrying only what changed.</summary>
    public static Message Partial(string id, bool? isRead = null, InferenceClassificationType? classification = null) => new()
    {
        Id = id,
        IsRead = isRead,
        InferenceClassification = classification,
    };

    public static Message Removed(string id) => new()
    {
        Id = id,
        AdditionalData = new Dictionary<string, object> { ["@removed"] = new { reason = "deleted" } },
    };

    public static MailDeltaPage Next(string nextLink, params Message[] messages) => new(messages, nextLink, null);

    public static MailDeltaPage Last(string deltaLink, params Message[] messages) => new(messages, null, deltaLink);

    /// <summary>A local list as if these messages had been synced earlier, newest first.</summary>
    public static ObservableCollection<MailData> Local(params Message[] messages) =>
        new(messages.Select(message => new MailData(message)).OrderByDescending(mail => mail.ReceivedDateTime));

    public static string[] Ids(IEnumerable<MailData> items) => items.Select(mail => mail.Id).ToArray();
}
