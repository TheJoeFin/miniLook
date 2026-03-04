using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph;
using Microsoft.Graph.Me.MailFolders.Item.Messages.Delta;
using Microsoft.Graph.Models;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;


namespace miniLook.ViewModels;

public partial class ListDetailsViewModel : ObservableRecipient, INavigationAware
{
    private bool loadedMail = false;

    [ObservableProperty]
    private MailData? selected;

    [ObservableProperty]
    private string accountName = string.Empty;

    [ObservableProperty]
    private int numberUnread = 0;

    [ObservableProperty]
    private bool isLoadingContent = false;

    [ObservableProperty]
    private DateTime lastSync = DateTime.MinValue;

    [ObservableProperty]
    private bool hasInternet = true;

    [ObservableProperty]
    private bool isFocusedView = true;

    [ObservableProperty]
    private bool hasOlderMail = false;

    private int mailWindowMonths = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFocusedUnread))]
    private int focusedUnreadCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtherUnread))]
    private int otherUnreadCount = 0;

    public bool HasFocusedUnread => FocusedUnreadCount > 0;
    public bool HasOtherUnread => OtherUnreadCount > 0;

    public ObservableCollection<MailData> MailItems { get; private set; } = [];

    public ObservableCollection<ConversationGroup> ConversationGroups { get; private set; } = [];

    public ObservableCollection<ConversationGroup> FilteredConversationGroups { get; private set; } = [];

    public ObservableCollection<Event> Events { get; private set; } = [];

    private GraphServiceClient? _graphClient;

    public bool HasGraphClient => _graphClient is not null;

    private string? deltaLink = null;
    private bool isSyncingMail = false;
    private bool isSigningOut = false;

    [ObservableProperty]
    private string debugText = $"{DateTime.Now:d} debug text begins";

    private INavigationService NavigationService { get; }

    private IMailCacheService MailCacheService { get; }

    private IGraphService GraphService { get; }

    public ListDetailsViewModel(INavigationService navigationService, IMailCacheService mailCacheService, IGraphService graphService)
    {
        NavigationService = navigationService;
        MailCacheService = mailCacheService;
        GraphService = graphService;
    }

    partial void OnDebugTextChanged(string value)
    {
        if (value.Length > 10000)
            DebugText = value.Substring(0, 5000);
    }

    partial void OnIsFocusedViewChanged(bool value)
    {
        RebuildFilteredConversationGroups();
    }

    private void MailItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateItems();
    }

    public void UpdateItems()
    {
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
        FocusedUnreadCount = MailItems.Where(m => !m.IsRead && m.IsFocused).Count();
        OtherUnreadCount = MailItems.Where(m => !m.IsRead && !m.IsFocused).Count();
        if (!IsLoadingContent)
            RunBackgroundSync();
    }

    public void RebuildConversationGroups()
    {
        Dictionary<string, List<MailData>> newGroupData = MailItems
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.ReceivedDateTime).ToList());

        List<string> desiredOrder = newGroupData
            .OrderByDescending(kvp => kvp.Value[0].ReceivedDateTime)
            .Select(kvp => kvp.Key)
            .ToList();

        HashSet<string> desiredIds = desiredOrder.ToHashSet();

        // Remove groups that no longer exist
        for (int i = ConversationGroups.Count - 1; i >= 0; i--)
        {
            if (!desiredIds.Contains(ConversationGroups[i].ConversationId))
                ConversationGroups.RemoveAt(i);
        }

        // Update existing groups and insert new ones in the correct order
        for (int targetIndex = 0; targetIndex < desiredOrder.Count; targetIndex++)
        {
            string conversationId = desiredOrder[targetIndex];
            List<MailData> messages = newGroupData[conversationId];

            int currentIndex = -1;
            for (int j = 0; j < ConversationGroups.Count; j++)
            {
                if (ConversationGroups[j].ConversationId == conversationId)
                {
                    currentIndex = j;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                ConversationGroups[currentIndex].SyncMessages(messages);
                if (currentIndex != targetIndex)
                    ConversationGroups.Move(currentIndex, targetIndex);
            }
            else
            {
                ConversationGroups.Insert(targetIndex, new ConversationGroup(conversationId, messages));
            }
        }

        RebuildFilteredConversationGroups();
    }

    public void RebuildFilteredConversationGroups()
    {
        List<ConversationGroup> filtered = ConversationGroups
            .Where(g => g.IsFocused == IsFocusedView)
            .ToList();

        for (int i = FilteredConversationGroups.Count - 1; i >= 0; i--)
        {
            if (!filtered.Contains(FilteredConversationGroups[i]))
                FilteredConversationGroups.RemoveAt(i);
        }

        for (int i = 0; i < filtered.Count; i++)
        {
            int currentIndex = FilteredConversationGroups.IndexOf(filtered[i]);
            if (currentIndex < 0)
                FilteredConversationGroups.Insert(i, filtered[i]);
            else if (currentIndex != i)
                FilteredConversationGroups.Move(currentIndex, i);
        }
    }

    [RelayCommand]
    private void ToggleFocusedView()
    {
        IsFocusedView = !IsFocusedView;
    }

    public async void RunBackgroundSync()
    {
        HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;

        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Check new timer tick\n");
        if (_graphClient is null)
        {
            DebugText = DebugText.Insert(0, $"Graph Client is null, returning\n");

            if (HasInternet)
                _graphClient = GraphService.Client;

            return;
        }

        Debug.WriteLine("Checking for new mail");
        DebugText = DebugText.Insert(0, $"Checking for new mail\n");
        IsLoadingContent = true;
        await GetEvents();
        await SyncMail();
        await CheckForOlderMailAsync();

        IsLoadingContent = false;
    }

    public async void OnNavigatedTo(object parameter)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Navigated to ListView Detail Page\n");
        isSigningOut = false;

        // For singleton usage: don't clear data if already loaded (flyout re-open)
        if (loadedMail && MailItems.Count > 0)
        {
            HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;
            return;
        }

        MailItems.Clear();
        Events.Clear();

        HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;

        GraphService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        GraphService.AuthenticationStateChanged += OnAuthenticationStateChanged;

        MailItems.CollectionChanged -= MailItems_CollectionChanged;
        MailItems.CollectionChanged += MailItems_CollectionChanged;

        Selected = parameter as MailData;

        await TryToLoadMail(Selected);
    }

    [RelayCommand]
    private void GoToSendMail()
    {
        NavigationService.NavigateTo(typeof(SendMailViewModel).FullName!);
    }

    [RelayCommand]
    private static void GoToOutlook()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://outlook.live.com/mail/0/"));
    }

    [RelayCommand]
    private void Refresh()
    {
        if (!IsLoadingContent)
            RunBackgroundSync();
    }

    public async Task ClearOutContents()
    {
        MailItems.Clear();
        ConversationGroups.Clear();
        FilteredConversationGroups.Clear();
        Events.Clear();
        App.SetUpcomingEvents([]);

        deltaLink = null;

        MailCacheService.DeltaLink = null;
        await MailCacheService.ClearMailCacheAsync();
        await MailCacheService.SaveDeltaLink(null);

        loadedMail = false;
    }

    [RelayCommand]
    private async Task SignOut()
    {
        isSigningOut = true;
        await ClearOutContents();
        await GraphService.SignOutAsync();
        _graphClient = null;
        NavigationService.NavigateTo(typeof(WelcomeViewModel).FullName!);
    }

    [RelayCommand]
    private async Task SignIn()
    {
        await GraphService.SignInAsync();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
    }

    [RelayCommand]
    private async Task ArchiveItem(object clickedItem)
    {
        if (clickedItem is not MailData listDetailsMenuItem)
            return;

        await ArchiveThisMailItem(listDetailsMenuItem);
    }

    [RelayCommand]
    private async Task ArchiveConversation(object clickedItem)
    {
        if (clickedItem is not ConversationGroup group)
            return;

        await ArchiveThisMailItem(group.LatestMessage);
    }

    [RelayCommand]
    private void MarkConversationAsRead(object clickedItem)
    {
        if (clickedItem is not ConversationGroup group)
            return;

        foreach (MailData message in group.Messages)
            MarkMessageIsReadAs(message, true);
    }

    [RelayCommand]
    private void ToggleConversationRead(object clickedItem)
    {
        if (clickedItem is not ConversationGroup group)
            return;

        bool newIsRead = !group.LatestMessage.IsRead;
        foreach (MailData message in group.Messages)
            MarkMessageIsReadAs(message, newIsRead);
    }

    [RelayCommand]
    private void PopOutConversation(object clickedItem)
    {
        if (clickedItem is not ConversationGroup group)
            return;

        MailData mail = group.LatestMessage;
        string mailId = mail.Id ?? string.Empty;

        if (App.MailItemWindows.TryGetValue(mailId, out Views.MailItemWindow? existing))
        {
            existing.Activate();
            return;
        }

        Views.MailItemWindow window = new(mail);
        App.MailItemWindows[mailId] = window;
        window.Closed += (s, e) => App.MailItemWindows.Remove(mailId);
        window.Activate();
    }

    public void PopOutMailItem(MailData mail)
    {
        string mailId = mail.Id ?? string.Empty;

        if (App.MailItemWindows.TryGetValue(mailId, out Views.MailItemWindow? existing))
        {
            existing.Activate();
            return;
        }

        Views.MailItemWindow window = new(mail);
        App.MailItemWindows[mailId] = window;
        window.Closed += (s, e) => App.MailItemWindows.Remove(mailId);
        window.Activate();
    }

    public async Task ArchiveThisMailItem(MailData listDetailsMenuItem)
    {
        if (_graphClient is null)
            return;

        MailFolderCollectionResponse? foldersResponse = await _graphClient.Me
            .MailFolders
            .GetAsync(config =>
            {
                config.QueryParameters.Filter = "displayName eq 'Archive'";
            });

        MailFolder? archiveFolder = foldersResponse?.Value?.FirstOrDefault();

        if (archiveFolder is null)
            return;

        List<MailData> allOfConversation = [.. MailItems.Where(m => m.ConversationId == listDetailsMenuItem.ConversationId)];

        foreach (MailData conversationItem in allOfConversation)
            MailItems.Remove(conversationItem);

        RebuildConversationGroups();

        foreach (MailData conversationItem in allOfConversation)
        {
            try
            {
                _ = await _graphClient.Me
                    .MailFolders["Inbox"]
                    .Messages[conversationItem.Id]
                    .Move
                    .PostAsync(new Microsoft.Graph.Me.MailFolders.Item.Messages.Item.Move.MovePostRequestBody
                    {
                        DestinationId = archiveFolder.Id
                    });
            }
            catch (Exception)
            {
#if DEBUG
                throw;
#endif
            }
        }
    }

    [RelayCommand]
    private void MarkMessageIsRead(object clickedItem)
    {
        if (clickedItem is not MailData listDetailsMenuItem)
            return;

        MarkMessageIsReadAs(listDetailsMenuItem, true);
    }

    public void MarkMessageIsReadAs(MailData? listDetailsMenuItem, bool isRead)
    {
        if (_graphClient is null || listDetailsMenuItem is null)
            return;

        listDetailsMenuItem.IsRead = isRead;
        FocusedUnreadCount = MailItems.Where(m => !m.IsRead && m.IsFocused).Count();
        OtherUnreadCount = MailItems.Where(m => !m.IsRead && !m.IsFocused).Count();

        _ = _graphClient.Me
            .MailFolders["Inbox"]
            .Messages[listDetailsMenuItem.Id]
            .PatchAsync(new Message { IsRead = isRead });
    }

    public async Task DeleteThisMailItem(MailData listDetailsMenuItem)
    {
        if (_graphClient is null)
            return;

        MailFolderCollectionResponse? foldersResponse = await _graphClient.Me
            .MailFolders
            .GetAsync(config =>
            {
                config.QueryParameters.Filter = "displayName eq 'Deleted Items'";
            });

        MailFolder? deletedFolder = foldersResponse?.Value?.FirstOrDefault();

        if (deletedFolder is null)
            return;

        try
        {
            // Move to Deleted Items instead of hard delete (matches Outlook behavior)
            _ = await _graphClient.Me
                .MailFolders["Inbox"]
                .Messages[listDetailsMenuItem.Id]
                .Move
                .PostAsync(new Microsoft.Graph.Me.MailFolders.Item.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = deletedFolder.Id
                });
        }
        catch (Exception)
        {
#if DEBUG
            throw;
#endif
        }

        MailItems.Remove(listDetailsMenuItem);
        RebuildConversationGroups();
    }

    [RelayCommand]
    private void NavigateToDetail(object clickedItem)
    {
        if (clickedItem is MailData mailData)
            NavigateToMailDetail(mailData);
    }

    public void NavigateToMailDetail(MailData mailData)
    {
        NavigationService.NavigateTo(typeof(MailDetailViewModel).FullName!, mailData);
    }

    public void RenderMailBody(MailData ListDetailsMenuItem)
    {
        NavigationService.NavigateTo(typeof(RenderWebViewViewModel).FullName!, ListDetailsMenuItem);
    }

    public async Task<byte[]?> GetAttachmentContentAsync(string messageId, string attachmentId)
    {
        if (_graphClient is null)
            return null;

        try
        {
            Attachment? attachment = await _graphClient.Me.Messages[messageId].Attachments[attachmentId].GetAsync();
            if (attachment is FileAttachment fileAttachment)
                return fileAttachment.ContentBytes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Attachments] Failed to fetch content for attachment {attachmentId}: {ex.Message}");
        }

        return null;
    }

    public async Task<string> GetHtmlBodyAsync(string messageId)
    {
        if (_graphClient is null)
            return string.Empty;

        try
        {
            Message? message = await _graphClient.Me.Messages[messageId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["body"];
            });

            if (message?.Body is null)
                return string.Empty;

            if (message.Body.ContentType == BodyType.Html)
                return message.Body.Content ?? string.Empty;

            // Plain-text email: wrap in <pre> so it displays correctly in WebView
            string escaped = System.Net.WebUtility.HtmlEncode(message.Body.Content ?? string.Empty);
            return $"<html><body><pre style='white-space:pre-wrap;font-family:sans-serif;'>{escaped}</pre></body></html>";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HtmlBody] Failed to fetch HTML body for {messageId}: {ex.Message}");
        }

        return string.Empty;
    }

    public async void RenderHtmlBody(MailData mail)
    {
        string mailId = mail.Id ?? string.Empty;

        if (App.HtmlViewWindows.TryGetValue(mailId, out Views.HtmlViewWindow? existing))
        {
            existing.Activate();
            return;
        }

        string htmlBody = await GetHtmlBodyAsync(mailId);
        if (string.IsNullOrEmpty(htmlBody))
            return;

        Views.HtmlViewWindow window = new();
        window.Title = mail.Subject ?? "miniLook";
        App.HtmlViewWindows[mailId] = window;
        window.Closed += (s, e) => App.HtmlViewWindows.Remove(mailId);
        window.Activate();
        _ = window.SetContent(htmlBody);
    }

    private async Task TryToLoadMail(MailData? selectMailData = null)
    {
        if (loadedMail)
            return;

        loadedMail = true;

        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Trying to load mail\n");
        if (!GraphService.IsAuthenticated || GraphService.Client is null)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Not authenticated, triggering sign-in\n");
            loadedMail = false;
            await GraphService.SignInAsync();
            return;
        }

        IsLoadingContent = true;

        await MailCacheService.InitializeAsync();
        mailWindowMonths = MailCacheService.MailWindowMonths;

        IEnumerable<MailData> tempMailItems = await MailCacheService.GetEmailsAsync();

        foreach (MailData mail in tempMailItems)
            MailItems.Add(mail);

        deltaLink = MailCacheService.DeltaLink;

        RebuildConversationGroups();

        Selected = selectMailData;

        if (!HasInternet)
        {
            IsLoadingContent = false;
            return;
        }

        _graphClient = GraphService.Client;
        User? me = await _graphClient.Me.GetAsync();
        AccountName = me?.DisplayName ?? string.Empty;

        await GetEvents();
        await SyncMail();
        await CheckForOlderMailAsync();

        IsLoadingContent = false;
        await MailCacheService.SaveEmailsAsync(MailItems);
    }

    private async Task SyncMail()
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Syncing Mail\n");
        if (_graphClient is null
            || !HasInternet
            || isSigningOut
            || !GraphService.IsAuthenticated
            || isSyncingMail)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Graph client is null {_graphClient is null} or isSyncingMail {isSyncingMail} caused return\n");
            return;
        }

        isSyncingMail = true;

        try
        {
            DateTime syncCutoff = DateTime.UtcNow.AddMonths(-mailWindowMonths);
            string initialSyncFilter = "receivedDateTime ge " + syncCutoff.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";
            DeltaGetResponse? currentPage;

            if (deltaLink is not null)
            {
                Debug.WriteLine($"[Sync] Using existing delta link: {deltaLink[..Math.Min(80, deltaLink.Length)]}...");
                DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Using existing delta link\n");
                try
                {
                    currentPage = await new DeltaRequestBuilder(deltaLink, _graphClient.RequestAdapter).GetAsDeltaGetResponseAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Sync] Delta link request failed, resetting to full sync: {ex.Message}");
                    DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Delta link failed, doing full sync\n");
                    deltaLink = null;
                    MailCacheService.DeltaLink = null;
                    currentPage = await _graphClient.Me.MailFolders["Inbox"].Messages.Delta.GetAsDeltaGetResponseAsync(config =>
                    {
                        config.QueryParameters.Select = ["id", "isRead", "sender", "toRecipients", "ccRecipients", "subject", "bodyPreview", "webLink", "receivedDateTime", "conversationId", "inferenceClassification", "hasAttachments"];
                        config.QueryParameters.Filter = initialSyncFilter;
                    });
                }
            }
            else
            {
                Debug.WriteLine("[Sync] No delta link, starting full delta sync.");
                DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: No delta link, starting full sync\n");
                currentPage = await _graphClient.Me.MailFolders["Inbox"].Messages.Delta.GetAsDeltaGetResponseAsync(config =>
                {
                    config.QueryParameters.Select = ["id", "isRead", "sender", "toRecipients", "ccRecipients", "subject", "bodyPreview", "webLink", "receivedDateTime", "conversationId", "inferenceClassification", "hasAttachments"];
                    config.QueryParameters.Filter = initialSyncFilter;
                });
            }

            int pageNumber = 0;

            do
            {
                pageNumber++;

                if (currentPage?.Value is null)
                {
                    Debug.WriteLine($"[Sync] Page {pageNumber}: Value is null, stopping pagination.");
                    DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Page {pageNumber} Value is null, stopping\n");
                    break;
                }

                DateTimeOffset? oldest = currentPage.Value
                    .Where(m => m.ReceivedDateTime is not null)
                    .Min(m => m.ReceivedDateTime);
                DateTimeOffset? newest = currentPage.Value
                    .Where(m => m.ReceivedDateTime is not null)
                    .Max(m => m.ReceivedDateTime);

                Debug.WriteLine($"[Sync] Page {pageNumber}: {currentPage.Value.Count} messages, oldest={oldest}, newest={newest}, hasNextLink={currentPage.OdataNextLink is not null}, hasDeltaLink={currentPage.OdataDeltaLink is not null}");
                DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Page {pageNumber}: {currentPage.Value.Count} msgs, oldest={oldest:g}, newest={newest:g}\n");

                int addedCount = 0;
                int updatedCount = 0;
                int removedCount = 0;
                int skippedSparseCount = 0;

                foreach (Message message in currentPage.Value)
                {
                    if (isSigningOut || !HasInternet)
                        return;

                    MailData? matchingMessage = MailItems.FirstOrDefault(m => m.Id == message.Id);

                    if (message.AdditionalData is not null
                        && message.AdditionalData.TryGetValue("@removed", out object? removed))
                    {
                        if (matchingMessage is not null)
                        {
                            MailItems.Remove(matchingMessage);
                            removedCount++;
                        }

                        continue;
                    }

                    // Sparse delta update: only changed properties, not a full message.
                    // A full message always has ReceivedDateTime; sparse updates do not.
                    if (message.ReceivedDateTime is null)
                    {
                        if (matchingMessage is not null)
                        {
                            if (message.IsRead is not null)
                                matchingMessage.IsRead = (bool)message.IsRead;
                            if (message.InferenceClassification is not null)
                                matchingMessage.IsFocused = message.InferenceClassification != InferenceClassificationType.Other;
                            updatedCount++;
                        }

                        skippedSparseCount++;
                        continue;
                    }

                    MailData newMail = new(message);

                    if (message.OdataType == "#microsoft.graph.eventMessageRequest")
                    {
                        newMail.IsEvent = true;
                    }

                    if (message.HasAttachments is true)
                    {
                        try
                        {
                            AttachmentCollectionResponse? result = await _graphClient
                                .Me.Messages[message.Id]
                                .Attachments.GetAsync();

                            if (result?.Value is not null)
                            {
                                newMail.AttachmentsCount = result.Value.Count;

                                foreach (Attachment attachment in result.Value)
                                {
                                    if (attachment is FileAttachment fileAttachment)
                                    {
                                        newMail.Attachments.Add(new Models.AttachmentInfo
                                        {
                                            Id = fileAttachment.Id ?? string.Empty,
                                            Name = fileAttachment.Name ?? string.Empty,
                                            ContentType = fileAttachment.ContentType ?? string.Empty,
                                        });
                                        Debug.WriteLine("File attachment found: " + fileAttachment.Name);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Sync] Failed to fetch attachments for message {message.Id}: {ex.Message}");
                        }
                    }

                    if (MailItems.Count == 0)
                    {
                        MailItems.Add(newMail);
                        addedCount++;
                        continue;
                    }

                    if (matchingMessage is not null)
                    {
                        if (message.InferenceClassification is not null)
                            matchingMessage.IsFocused = message.InferenceClassification != InferenceClassificationType.Other;
                        updatedCount++;
                        continue;
                    }

                    bool insertedMail = false;
                    for (int i = 0; i < MailItems.Count; i++)
                    {
                        if (MailItems[i].ReceivedDateTime < message.ReceivedDateTime)
                        {
                            MailItems.Insert(i, newMail);
                            insertedMail = true;
                            break;
                        }
                    }

                    if (!insertedMail)
                        MailItems.Add(newMail);

                    addedCount++;
                }

                Debug.WriteLine($"[Sync] Page {pageNumber} processed: added={addedCount}, updated={updatedCount}, removed={removedCount}, sparseSkipped={skippedSparseCount}");
                DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Page {pageNumber} done: +{addedCount} ~{updatedCount} -{removedCount} sparse={skippedSparseCount}\n");

                if (currentPage.OdataNextLink is not null)
                {
                    Debug.WriteLine($"[Sync] Fetching next page {pageNumber + 1}...");
                    try
                    {
                        currentPage = await new DeltaRequestBuilder(currentPage.OdataNextLink, _graphClient.RequestAdapter).GetAsDeltaGetResponseAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Sync] Failed to fetch page {pageNumber + 1}: {ex.Message}");
                        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Failed to fetch page {pageNumber + 1}: {ex.Message}\n");
                        break;
                    }
                }
                else
                {
                    Debug.WriteLine($"[Sync] No more pages after page {pageNumber}. HasDeltaLink={currentPage.OdataDeltaLink is not null}");
                    DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: No more pages after page {pageNumber}\n");
                    break;
                }
            }
            while (currentPage is not null);

            Debug.WriteLine($"[Sync] Pagination complete after {pageNumber} pages. Total MailItems={MailItems.Count}. HasDeltaLink={currentPage?.OdataDeltaLink is not null}");
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Done {pageNumber} pages, {MailItems.Count} total items, deltaLink={currentPage?.OdataDeltaLink is not null}\n");

            if (currentPage?.OdataDeltaLink is not null)
            {
                deltaLink = currentPage.OdataDeltaLink;
                await MailCacheService.SaveDeltaLink(deltaLink);
                await MailCacheService.SaveEmailsAsync(MailItems);
            }
            else
            {
                Debug.WriteLine("[Sync] WARNING: No delta link received — next sync will do a full re-fetch.");
                DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: WARNING no delta link received\n");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] Mail sync failed: {ex.Message}");
        }
        finally
        {
            isSyncingMail = false;
            LastSync = DateTime.Now;
            NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
            App.SetTaskbarBadgeToNumber(NumberUnread);
            RebuildConversationGroups();
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Mail synced\n");
        }
    }

    private async Task CheckForOlderMailAsync()
    {
        if (_graphClient is null || !HasInternet || isSigningOut)
            return;

        DateTime cutoff = DateTime.UtcNow.AddMonths(-mailWindowMonths);
        string isoDate = cutoff.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";
        try
        {
            MessageCollectionResponse? resp = await _graphClient.Me.MailFolders["Inbox"].Messages.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"receivedDateTime lt {isoDate}";
                config.QueryParameters.Top = 1;
                config.QueryParameters.Select = ["id"];
            });
            HasOlderMail = resp?.Value?.Count > 0;
        }
        catch { }
    }

    [RelayCommand]
    private async Task LoadMoreMonths()
    {
        if (IsLoadingContent || _graphClient is null)
            return;

        mailWindowMonths++;
        await MailCacheService.SaveMailWindowMonthsAsync(mailWindowMonths);

        deltaLink = null;
        await MailCacheService.SaveDeltaLink(null);

        IsLoadingContent = true;
        await SyncMail();
        await CheckForOlderMailAsync();
        IsLoadingContent = false;
        await MailCacheService.SaveEmailsAsync(MailItems);
    }

    private async Task GetEvents()
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Getting Events\n");
        if (isSigningOut
            || !HasInternet
            || _graphClient is null)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Graph client is null {_graphClient is null} caused return\n");
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime endOfWeek = now.AddDays(2);

        try
        {
            // Get the user's mailbox settings to determine their time zone
            User? user = await _graphClient.Me.GetAsync(config =>
            {
                config.QueryParameters.Select = ["mailboxSettings"];
            });

            string timeZone = user?.MailboxSettings?.TimeZone ?? "UTC";

            EventCollectionResponse? eventsResponse = await _graphClient.Me.CalendarView.GetAsync(config =>
            {
                config.QueryParameters.StartDateTime = now.ToString("o");
                config.QueryParameters.EndDateTime = endOfWeek.ToString("o");
                config.QueryParameters.Orderby = ["start/dateTime"];
                config.QueryParameters.Top = 4;
                config.Headers.Add("Prefer", $"outlook.timezone=\"{timeZone}\"");
            });

            Events.Clear();

            if (eventsResponse?.Value is not null)
            {
                foreach (Event ev in eventsResponse.Value)
                    Events.Add(ev);
            }
        }
        catch (Exception)
        {
            return;
        }

        App.SetUpcomingEvents(Events);
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Events gotten\n");
    }

    public void OnNavigatedFrom()
    {
        App.SetTaskbarBadgeToNumber(NumberUnread);
    }

    private async void OnAuthenticationStateChanged(object? sender, bool isSignedIn)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Auth state changed to {isSignedIn}\n");
        if (!isSignedIn)
            return;

        if (!loadedMail && GraphService.IsAuthenticated)
            await TryToLoadMail();
    }

    public void ReplyToThisMailItem(MailData? listDetailsMenuItem)
    {
        NavigationService.NavigateTo(typeof(SendMailViewModel).FullName!, (listDetailsMenuItem, MessageActionFlag.Reply));
    }

    public void ForwardThisMailItem(MailData? listDetailsMenuItem)
    {
        NavigationService.NavigateTo(typeof(SendMailViewModel).FullName!, (listDetailsMenuItem, MessageActionFlag.Forward));
    }


}
