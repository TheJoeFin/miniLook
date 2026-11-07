using System.Diagnostics;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using miniLook.Contracts.Services;
using miniLook.Core.Helpers;
using miniLook.Models;
using AttachmentInfo = miniLook.Models.AttachmentInfo;

namespace miniLook.Services;

/// <summary>
/// Keeps a local mail list matched to the inbox using Graph delta rounds. It covers three cases:
/// a first sync, an incremental round from the saved cursor, and a resync after the cursor expired.
/// A first sync and a resync are the same thing: list everything in the window, then drop what was not listed.
/// A canceled or failed round keeps its place in memory, so the next pass continues from the last page.
/// </summary>
public sealed class MailSyncEngine
{
    private readonly IMailCacheService _cache;
    private readonly HashSet<string> _attachmentsUnavailable = new(StringComparer.Ordinal);
    private Round? _round;
    private bool _cacheDirty;
    private bool _cursorResetThisPass;

    public MailSyncEngine(IMailCacheService cache)
    {
        _cache = cache;
    }

    public Action<string>? Log { get; set; }

    public void MarkCacheDirty() => _cacheDirty = true;

    public void Reset()
    {
        _round = null;
        _cacheDirty = false;
        _attachmentsUnavailable.Clear();
    }

    /// <summary>
    /// Forgets the saved cursor before listing again, so an interrupted run still re-lists
    /// after a restart instead of quietly carrying on with the old, narrower filter.
    /// </summary>
    public async Task RequestFullEnumerationAsync()
    {
        await _cache.SaveDeltaLink(null);
        _round = null;
    }

    /// <summary>Makes a loaded cache safe for the sync, which expects unique IDs in newest-first order.</summary>
    public static List<MailData> NormalizeCached(IEnumerable<MailData> cached) => cached
        .Where(mail => !string.IsNullOrEmpty(mail.Id))
        .GroupBy(mail => mail.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderByDescending(mail => mail.ReceivedDateTime)
        .ToList();

    /// <summary>Inserts into a newest-first list, after any mail with the same time.</summary>
    public static void InsertNewestFirst(IList<MailData> items, MailData mail)
    {
        int low = 0;
        int high = items.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (items[middle].ReceivedDateTime >= mail.ReceivedDateTime)
                low = middle + 1;
            else
                high = middle;
        }

        items.Insert(low, mail);
    }

    public static async Task LoadAttachmentsAsync(IMailSyncClient client, MailData mail, CancellationToken cancellationToken)
    {
        IReadOnlyList<Attachment> attachments = await client.GetAttachmentsAsync(mail.Id, cancellationToken);
        List<AttachmentInfo> files = attachments
            .Where(attachment => attachment is not (ItemAttachment or ReferenceAttachment))
            .Select(attachment => new AttachmentInfo
            {
                Id = attachment.Id ?? string.Empty,
                Name = attachment.Name ?? string.Empty,
                ContentType = attachment.ContentType ?? string.Empty,
            })
            .ToList();
        mail.SetAttachments(files, attachments.Count);
    }

    /// <param name="onMailChanged">Runs after each page that changed the list, so long syncs can show progress.</param>
    public async Task SyncAsync(
        IMailSyncClient client,
        IList<MailData> items,
        int windowMonths,
        DateTimeOffset now,
        Action? onMailChanged,
        CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = MailSyncPolicy.GetCutoff(now, windowMonths);
        Dictionary<string, MailData> index = new(StringComparer.Ordinal);
        foreach (MailData mail in items)
            index[mail.Id] = mail;

        _cursorResetThisPass = false;
        _round ??= new Round(listsEverything: _cache.DeltaLink is null);
        WriteLog(_round.ListsEverything ? "Listing all mail in the window" : "Checking for changes since the last sync");

        int pageNumber = 0;
        MailDeltaPage page = await FetchNextPageAsync(client, cutoff, cancellationToken);
        while (true)
        {
            pageNumber++;
            ApplyPage(page, items, index, _round, cutoff, pageNumber, onMailChanged);

            if (page.DeltaLink is not null)
                break;
            if (page.NextLink is null)
                throw new InvalidDataException("Graph completed a delta page without a next link or a delta link.");

            _round.NextLink = page.NextLink;
            page = await FetchNextPageAsync(client, cutoff, cancellationToken);
        }

        await CompleteRoundAsync(page.DeltaLink, items, cutoff, onMailChanged, cancellationToken);

        try
        {
            await BackfillAttachmentsAsync(client, items, onMailChanged, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not SignInRequiredException)
        {
            // The mail itself is already up to date and saved. The next pass carries on from here.
            WriteLog($"Attachment listings paused: {ex.Message}");
        }
    }

    private async Task<MailDeltaPage> FetchNextPageAsync(IMailSyncClient client, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        Round round = _round!;
        string? link = round.NextLink ?? (round.ListsEverything ? null : _cache.DeltaLink);
        if (link is null)
        {
            _round = new Round(listsEverything: true);
            return await client.GetInitialPageAsync(cutoff, cancellationToken);
        }

        try
        {
            return await client.GetLinkedPageAsync(link, cancellationToken);
        }
        catch (MailSyncCursorExpiredException) when (!_cursorResetThisPass)
        {
            // Graph ages out old delta tokens, so this is the normal path after a long absence.
            // A next link can expire too, which is why the check sits here and not only on the first request.
            _cursorResetThisPass = true;
            WriteLog("The saved sync position expired, so all mail in the window will be listed again");
            await _cache.SaveDeltaLink(null);
            _round = new Round(listsEverything: true);
            return await client.GetInitialPageAsync(cutoff, cancellationToken);
        }
    }

    private void ApplyPage(
        MailDeltaPage page,
        IList<MailData> items,
        Dictionary<string, MailData> index,
        Round round,
        DateTimeOffset cutoff,
        int pageNumber,
        Action? onMailChanged)
    {
        int added = 0;
        int updated = 0;
        int removed = 0;
        bool changed = false;

        // Graph allows replays and the same message more than once in a round, so every step is safe to repeat.
        foreach (Message message in page.Messages)
        {
            if (string.IsNullOrEmpty(message.Id))
            {
                Debug.WriteLine("[Sync] Skipped a delta entry without an ID.");
                continue;
            }

            index.TryGetValue(message.Id, out MailData? existing);

            if (message.AdditionalData?.ContainsKey("@removed") == true)
            {
                round.Listed?.Remove(message.Id);
                if (existing is not null)
                {
                    items.Remove(existing);
                    index.Remove(message.Id);
                    changed = true;
                    removed++;
                }

                continue;
            }

            if (existing is not null)
            {
                round.Listed?.Add(message.Id);
                if (existing.ApplyDelta(message))
                {
                    changed = true;
                    updated++;
                }

                continue;
            }

            // Delta can report read-state changes on mail the filter excludes. A partial entry
            // or a message older than the window is not something the list should start showing.
            if (message.ReceivedDateTime is not DateTimeOffset received || received < cutoff)
                continue;

            MailData mail = new(message);
            InsertNewestFirst(items, mail);
            index[message.Id] = mail;
            round.Listed?.Add(message.Id);
            changed = true;
            added++;
        }

        WriteLog($"Page {pageNumber}: +{added} ~{updated} -{removed}");
        if (changed)
        {
            _cacheDirty = true;
            onMailChanged?.Invoke();
        }
    }

    private async Task CompleteRoundAsync(
        string deltaLink,
        IList<MailData> items,
        DateTimeOffset cutoff,
        Action? onMailChanged,
        CancellationToken cancellationToken)
    {
        Round round = _round!;
        int outdated = 0;

        // A full listing only reports what exists now, not what was deleted or moved while
        // nobody was looking. Anything local that it did not list is gone from the inbox.
        if (round.Listed is HashSet<string> listed)
            outdated += RemoveWhere(items, mail => !listed.Contains(mail.Id));

        // The delta filter is frozen when a round starts, so mail ages out of the window only here.
        outdated += RemoveWhere(items, mail => mail.ReceivedDateTime < cutoff);

        if (outdated > 0)
        {
            WriteLog($"Removed {outdated} messages that are no longer in the inbox window");
            onMailChanged?.Invoke();
        }

        if (outdated > 0 || round.ListsEverything)
            _cacheDirty = true;

        // The cache goes first. If the app stops in between, the old cursor replays changes
        // the cache already has, which is harmless. The other order would lose those changes.
        await SaveCacheIfDirtyAsync(items, cancellationToken);
        await _cache.SaveDeltaLink(deltaLink);
        _round = null;
        WriteLog($"Sync finished with {items.Count} messages");
    }

    private async Task BackfillAttachmentsAsync(
        IMailSyncClient client,
        IList<MailData> items,
        Action? onMailChanged,
        CancellationToken cancellationToken)
    {
        // Listings were left out of the page loop so they cannot slow it down or fail it.
        // The list is newest first, so recent mail gets its attachments first.
        List<MailData> batch = items
            .Where(mail => mail.AttachmentsPending && !_attachmentsUnavailable.Contains(mail.Id))
            .Take(MailSyncPolicy.AttachmentBatchSize)
            .ToList();

        foreach (MailData mail in batch)
        {
            try
            {
                await LoadAttachmentsAsync(client, mail, cancellationToken);
                _cacheDirty = true;
                onMailChanged?.Invoke();
            }
            catch (ApiException ex) when (MailSyncPolicy.IsServiceBusy(ex.ResponseStatusCode))
            {
                WriteLog($"Attachment listings paused: the service returned {ex.ResponseStatusCode}");
                break;
            }
            catch (Exception ex) when (ex is ApiException or InvalidDataException)
            {
                // One message Graph will not list must not hold up the others. Opening it retries.
                Debug.WriteLine($"[Sync] Could not list attachments for one message: {ex.Message}");
                _attachmentsUnavailable.Add(mail.Id);
            }
        }

        await SaveCacheIfDirtyAsync(items, cancellationToken);
    }

    private async Task SaveCacheIfDirtyAsync(IList<MailData> items, CancellationToken cancellationToken)
    {
        if (!_cacheDirty)
            return;

        // Cleared before the wait, so a change the user makes during the write is not forgotten.
        _cacheDirty = false;
        try
        {
            await _cache.SaveEmailsAsync(items, cancellationToken);
        }
        catch
        {
            _cacheDirty = true;
            throw;
        }
    }

    private static int RemoveWhere(IList<MailData> items, Func<MailData, bool> shouldRemove)
    {
        int removed = 0;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (shouldRemove(items[i]))
            {
                items.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    private void WriteLog(string message)
    {
        Debug.WriteLine($"[Sync] {message}");
        Log?.Invoke(message);
    }

    private sealed class Round(bool listsEverything)
    {
        /// <summary>IDs seen so far in a full listing, or null when this round only reports changes.</summary>
        public HashSet<string>? Listed { get; } = listsEverything ? new(StringComparer.Ordinal) : null;

        public bool ListsEverything => Listed is not null;

        /// <summary>Where to continue. Null until the first page has been applied.</summary>
        public string? NextLink { get; set; }
    }
}
