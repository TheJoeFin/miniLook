using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;
using miniLook.Core.Helpers;
using miniLook.Helpers;
using miniLook.Services;
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

    private static readonly TimeSpan MailListRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly MailSyncEngine _syncEngine;
    private readonly Stopwatch _sinceMailListRefresh = new();
    private bool isSigningOut = false;
    private CancellationTokenSource? _syncCancellation;
    private Task<SyncOutcome>? _syncTask;
    private bool _groupsDirty;
    private string? _mailboxTimeZone;
    private DateTime _lastEventsSync = DateTime.MinValue;
    private DateTime _lastOlderMailCheck = DateTime.MinValue;

    private string _syncError = string.Empty;
    public string SyncError
    {
        get => _syncError;
        set
        {
            if (SetProperty(ref _syncError, value))
                OnPropertyChanged(nameof(HasSyncError));
        }
    }

    public bool HasSyncError => !string.IsNullOrEmpty(SyncError);

    [ObservableProperty]
    private bool needsSignIn = false;

    [ObservableProperty]
    private string debugText = $"{DateTime.Now:d} debug text begins";

    private INavigationService NavigationService { get; }

    private IMailCacheService MailCacheService { get; }

    private ICalendarCacheService CalendarCacheService { get; }

    private IGraphService GraphService { get; }

    public ListDetailsViewModel(
        INavigationService navigationService,
        IMailCacheService mailCacheService,
        ICalendarCacheService calendarCacheService,
        IGraphService graphService)
    {
        NavigationService = navigationService;
        MailCacheService = mailCacheService;
        CalendarCacheService = calendarCacheService;
        GraphService = graphService;
        _syncEngine = new MailSyncEngine(mailCacheService)
        {
            Log = message => DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: {message}\n"),
        };
        MailItems.CollectionChanged += MailItems_CollectionChanged;
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
        if (!IsLoadingContent)
            UpdateItems();
    }

    public void UpdateItems()
    {
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
        FocusedUnreadCount = MailItems.Where(m => !m.IsRead && m.IsFocused).Count();
        OtherUnreadCount = MailItems.Where(m => !m.IsRead && !m.IsFocused).Count();
        App.SetUnreadMailCounts(NumberUnread, FocusedUnreadCount);
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
        Dictionary<string, ConversationGroup> existingGroups = ConversationGroups.ToDictionary(group => group.ConversationId);

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

            if (existingGroups.TryGetValue(conversationId, out ConversationGroup? existing))
            {
                existing.SyncMessages(messages);
                if (!ReferenceEquals(ConversationGroups[targetIndex], existing))
                    ConversationGroups.Move(ConversationGroups.IndexOf(existing), targetIndex);
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
        HashSet<ConversationGroup> desired = filtered.ToHashSet();

        for (int i = FilteredConversationGroups.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(FilteredConversationGroups[i]))
                FilteredConversationGroups.RemoveAt(i);
        }

        for (int i = 0; i < filtered.Count; i++)
        {
            if (i < FilteredConversationGroups.Count && ReferenceEquals(FilteredConversationGroups[i], filtered[i]))
                continue;
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

    public Task<SyncOutcome> RunBackgroundSyncAsync(bool foreground = true, bool force = false, bool extendHistory = false)
    {
        if (IsLoadingContent || isSigningOut || !GraphService.IsAuthenticated)
            return Task.FromResult(SyncOutcome.Skipped);

        _syncCancellation?.Dispose();
        _syncCancellation = new();
        _syncTask = RunSyncAsync(foreground, force, extendHistory, _syncCancellation.Token);
        return _syncTask;
    }

    public void CancelPendingSync() => _syncCancellation?.Cancel();

    public async Task CancelPendingSyncAsync()
    {
        CancelPendingSync();
        if (_syncTask is not null)
            await _syncTask;
    }

    private async Task<SyncOutcome> RunSyncAsync(bool foreground, bool force, bool extendHistory, CancellationToken cancellationToken)
    {
        IsLoadingContent = true;
        try
        {
            HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;
            _graphClient = GraphService.Client;
            if (_graphClient is null)
            {
                Debug.WriteLine("[Sync] Cannot check mail without an authenticated Graph client.");
                throw new InvalidOperationException("An authenticated Graph client is unavailable.");
            }

            Debug.WriteLine("Checking for new mail");
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Checking for new mail\n");
            if (!loadedMail)
                await TryToLoadMail(Selected);

            cancellationToken.ThrowIfCancellationRequested();
            if (extendHistory)
            {
                mailWindowMonths++;
                await MailCacheService.SaveMailWindowMonthsAsync(mailWindowMonths);
                await _syncEngine.RequestFullEnumerationAsync();
            }
            if (force || DateTime.UtcNow - _lastEventsSync >= PollingPolicy.GetCalendarInterval(foreground))
                await TryRefreshEventsAsync(cancellationToken);
            await SyncMail(cancellationToken);
            if (foreground && (force || extendHistory || DateTime.UtcNow - _lastOlderMailCheck >= TimeSpan.FromMinutes(5)))
                await TryCheckForOlderMailAsync(cancellationToken);
            LastSync = DateTime.Now;
            SyncError = string.Empty;
            NeedsSignIn = false;
            App.RefreshTrayState();
            return SyncOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("[Sync] Refresh canceled; the next pass continues from the last completed page.");
            return SyncOutcome.Skipped;
        }
        catch (SignInRequiredException)
        {
            Debug.WriteLine("[Sync] The saved sign-in expired; waiting for the user to sign in again.");
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Sign-in expired\n");
            OnSignInStateChanged(false);
            return SyncOutcome.Skipped;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] Background sync failed: {ex.Message}");
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Background sync failed: {ex.Message}\n");
            SyncError = "Sync_Error".GetLocalized();
            return SyncOutcome.Failed;
        }
        finally
        {
            if (_groupsDirty)
                RefreshMailList();
            IsLoadingContent = false;
        }
    }

    public void OnSignInStateChanged(bool isSignedIn)
    {
        NeedsSignIn = !isSignedIn && !isSigningOut;
        SyncError = NeedsSignIn ? "Sync_SignInRequired".GetLocalized() : string.Empty;
    }

    private void RefreshMailList()
    {
        UpdateItems();
        RebuildConversationGroups();
        _groupsDirty = false;
        _sinceMailListRefresh.Restart();
    }

    private void OnSyncChangedMail()
    {
        _groupsDirty = true;
        // Shows a long sync as it goes, newest mail first, without rebuilding the groups on every page.
        if (!_sinceMailListRefresh.IsRunning || _sinceMailListRefresh.Elapsed >= MailListRefreshInterval)
            RefreshMailList();
    }

    private async Task TryRefreshEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetEvents(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not SignInRequiredException)
        {
            // Calendar trouble must not stop mail from syncing. Try again at the next calendar interval.
            _lastEventsSync = DateTime.UtcNow;
            Debug.WriteLine($"[Sync] Calendar refresh failed: {ex.Message}");
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Calendar refresh failed: {ex.Message}\n");
        }
    }

    private async Task TryCheckForOlderMailAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckForOlderMailAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not SignInRequiredException)
        {
            // The mail is already synced, so a failed history check is not a failed refresh.
            _lastOlderMailCheck = DateTime.UtcNow;
            Debug.WriteLine($"[Sync] Older mail check failed: {ex.Message}");
        }
    }

    public async void OnNavigatedTo(object parameter)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Navigated to ListView Detail Page\n");
        isSigningOut = false;

        Selected = parameter as MailData;
        if (!loadedMail)
            await RunBackgroundSyncAsync();
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
    private async Task Refresh()
    {
        await RunBackgroundSyncAsync(force: true);
    }

    public async Task ClearOutContents()
    {
        await CancelPendingSyncAsync();
        IsLoadingContent = true;
        try
        {
            MailItems.Clear();
            ConversationGroups.Clear();
            FilteredConversationGroups.Clear();
            Events.Clear();
            UpdateItems();
            App.SetUpcomingEvents([]);

            _syncEngine.Reset();
            loadedMail = false;
            _mailboxTimeZone = null;
            _lastEventsSync = DateTime.MinValue;
            _lastOlderMailCheck = DateTime.MinValue;
            _groupsDirty = false;
            SyncError = string.Empty;
            NeedsSignIn = false;

            // The cursor goes first. A cache file left behind by a failed delete is then
            // corrected by a full listing rather than trusted, possibly for another account.
            MailCacheService.DeltaLink = null;
            await MailCacheService.SaveDeltaLink(null);
            await CalendarCacheService.ClearEventsAsync();
            await MailCacheService.ClearMailCacheAsync();
        }
        finally
        {
            IsLoadingContent = false;
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        isSigningOut = true;
        try
        {
            await ClearOutContents();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cache] Cached files could not all be removed during sign-out: {ex.Message}");
        }
        await GraphService.SignOutAsync();
        _graphClient = null;
        NavigationService.NavigateTo(typeof(WelcomeViewModel).FullName!);
    }

    [RelayCommand]
    private async Task SignIn()
    {
        try
        {
            await GraphService.SignInAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Auth] Sign-in failed: {ex.Message}");
        }
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

        string? archiveFolderId = await TryGetFolderIdAsync("Archive");
        if (archiveFolderId is null)
            return;

        List<MailData> allOfConversation = [.. MailItems.Where(m => m.ConversationId == listDetailsMenuItem.ConversationId)];

        foreach (MailData conversationItem in allOfConversation)
            MailItems.Remove(conversationItem);

        _syncEngine.MarkCacheDirty();
        RefreshMailList();

        foreach (MailData conversationItem in allOfConversation)
        {
            if (await TryMoveMessageAsync(conversationItem, archiveFolderId))
                continue;

            // The server still has this message, and a delta will never report it as new,
            // so it has to go back in the list now or it stays missing until a full resync.
            // A sync that ran during the move may already have put it back.
            if (!MailItems.Contains(conversationItem))
                MailSyncEngine.InsertNewestFirst(MailItems, conversationItem);
            RefreshMailList();
        }
    }

    private async Task<string?> TryGetFolderIdAsync(string displayName)
    {
        if (_graphClient is null)
            return null;

        try
        {
            MailFolderCollectionResponse? foldersResponse = await _graphClient.Me
                .MailFolders
                .GetAsync(config =>
                {
                    config.QueryParameters.Filter = $"displayName eq '{displayName}'";
                });

            return foldersResponse?.Value?.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mail] Could not find the {displayName} folder: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> TryMoveMessageAsync(MailData mail, string destinationFolderId)
    {
        if (_graphClient is null)
            return false;

        try
        {
            _ = await _graphClient.Me
                .MailFolders["Inbox"]
                .Messages[mail.Id]
                .Move
                .PostAsync(new Microsoft.Graph.Me.MailFolders.Item.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = destinationFolderId
                });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mail] Could not move a message: {ex.Message}");
            return false;
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

        if (listDetailsMenuItem.IsRead == isRead)
            return;
        listDetailsMenuItem.IsRead = isRead;
        _syncEngine.MarkCacheDirty();
        UpdateItems();

        _ = SaveReadStateAsync(_graphClient, listDetailsMenuItem, isRead);
    }

    private async Task SaveReadStateAsync(GraphServiceClient client, MailData mail, bool isRead)
    {
        try
        {
            await client.Me
                .MailFolders["Inbox"]
                .Messages[mail.Id]
                .PatchAsync(new Message { IsRead = isRead });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mail] Could not save the read state: {ex.Message}");
            // The server never changed, so no delta will correct this. Show what the server has.
            if (mail.IsRead == isRead)
            {
                mail.IsRead = !isRead;
                _syncEngine.MarkCacheDirty();
                UpdateItems();
            }
        }
    }

    public async Task DeleteThisMailItem(MailData listDetailsMenuItem)
    {
        if (_graphClient is null)
            return;

        string? deletedFolderId = await TryGetFolderIdAsync("Deleted Items");
        if (deletedFolderId is null)
            return;

        // Move to Deleted Items instead of hard delete (matches Outlook behavior)
        if (!await TryMoveMessageAsync(listDetailsMenuItem, deletedFolderId))
            return;

        MailItems.Remove(listDetailsMenuItem);
        _syncEngine.MarkCacheDirty();
        RefreshMailList();
    }

    /// <summary>Fills in attachments straight away when someone opens mail the sync has not reached yet.</summary>
    public async Task LoadPendingAttachmentsAsync(MailData mail, CancellationToken cancellationToken)
    {
        if (_graphClient is null || !mail.AttachmentsPending)
            return;

        try
        {
            await MailSyncEngine.LoadAttachmentsAsync(new GraphMailSyncClient(_graphClient), mail, cancellationToken);
            _syncEngine.MarkCacheDirty();
            _groupsDirty = true;
            if (!IsLoadingContent)
                RefreshMailList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("[Attachments] Listing canceled because the detail view changed.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Attachments] Could not list attachments: {ex.Message}");
        }
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

    public async Task<byte[]?> GetAttachmentContentAsync(string messageId, string attachmentId, CancellationToken cancellationToken = default)
    {
        if (_graphClient is null)
            return null;

        try
        {
            Attachment? attachment = await _graphClient.Me.Messages[messageId].Attachments[attachmentId].GetAsync(cancellationToken: cancellationToken);
            if (attachment is FileAttachment fileAttachment)
                return fileAttachment.ContentBytes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Attachments] Failed to fetch content for attachment {attachmentId}: {ex.Message}");
        }

        return null;
    }

    public async Task<string> GetHtmlBodyAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (_graphClient is null)
            return string.Empty;

        try
        {
            Message? message = await _graphClient.Me.Messages[messageId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["body"];
            }, cancellationToken: cancellationToken);

            if (message?.Body is null)
                return string.Empty;

            if (message.Body.ContentType == BodyType.Html)
                return message.Body.Content ?? string.Empty;

            // Plain-text email: wrap in <pre> so it displays correctly in WebView
            string escaped = System.Net.WebUtility.HtmlEncode(message.Body.Content ?? string.Empty);
            return $"<html><body><pre style='white-space:pre-wrap;font-family:sans-serif;'>{escaped}</pre></body></html>";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
        await window.SetContent(htmlBody);
    }

    private async Task TryToLoadMail(MailData? selectMailData = null)
    {
        if (loadedMail)
            return;

        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Trying to load mail\n");
        await MailCacheService.InitializeAsync();
        mailWindowMonths = MailCacheService.MailWindowMonths;

        IEnumerable<MailData> tempMailItems = await MailCacheService.GetEmailsAsync();

        MailItems.Clear();
        Events.Clear();
        foreach (MailData mail in MailSyncEngine.NormalizeCached(tempMailItems))
            MailItems.Add(mail);

        loadedMail = true;

        RebuildConversationGroups();
        UpdateItems();

        Selected = selectMailData;
    }

    private async Task SyncMail(CancellationToken cancellationToken)
    {
        if (_graphClient is null || isSigningOut || !GraphService.IsAuthenticated)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Skipped mail sync because no account is signed in\n");
            return;
        }

        await _syncEngine.SyncAsync(
            new GraphMailSyncClient(_graphClient),
            MailItems,
            mailWindowMonths,
            DateTimeOffset.UtcNow,
            OnSyncChangedMail,
            cancellationToken);
    }

    private async Task CheckForOlderMailAsync(CancellationToken cancellationToken)
    {
        if (_graphClient is null || isSigningOut)
            return;

        // Graph lists at most this many messages for a filtered delta, so a
        // wider window could not show any more mail and the offer would do nothing.
        if (MailItems.Count >= MailSyncPolicy.ServerEnumerationLimit)
        {
            HasOlderMail = false;
            _lastOlderMailCheck = DateTime.UtcNow;
            return;
        }

        DateTimeOffset cutoff = MailSyncPolicy.GetCutoff(DateTimeOffset.UtcNow, mailWindowMonths);
        MessageCollectionResponse? resp = await _graphClient.Me.MailFolders["Inbox"].Messages.GetAsync(config =>
        {
            config.QueryParameters.Filter = MailSyncPolicy.ReceivedBefore(cutoff);
            config.QueryParameters.Top = 1;
            config.QueryParameters.Select = ["id"];
        }, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resp?.Value is null)
            throw new InvalidDataException("Graph returned no history collection.");
        HasOlderMail = resp.Value.Count > 0;
        _lastOlderMailCheck = DateTime.UtcNow;
    }

    [RelayCommand]
    private async Task LoadMoreMonths()
    {
        if (_graphClient is null)
            return;

        // A running pass would otherwise make this click do nothing. Canceling loses no work.
        await CancelPendingSyncAsync();
        await RunBackgroundSyncAsync(extendHistory: true);
    }

    private async Task GetEvents(CancellationToken cancellationToken)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Getting Events\n");
        if (isSigningOut
            || _graphClient is null)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Graph client is null {_graphClient is null} caused return\n");
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime endOfWeek = now.AddDays(2);

        if (_mailboxTimeZone is null)
        {
            User? user = await _graphClient.Me.GetAsync(config =>
            {
                config.QueryParameters.Select = ["displayName", "mailboxSettings"];
            }, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (user is null)
                throw new InvalidDataException("Graph returned no mailbox profile.");
            AccountName = user.DisplayName ?? string.Empty;
            _mailboxTimeZone = user.MailboxSettings?.TimeZone ?? "UTC";
        }

        EventCollectionResponse? eventsResponse = await _graphClient.Me.CalendarView.GetAsync(config =>
        {
            config.QueryParameters.StartDateTime = now.ToString("o");
            config.QueryParameters.EndDateTime = endOfWeek.ToString("o");
            config.QueryParameters.Orderby = ["start/dateTime"];
            config.QueryParameters.Top = 4;
            config.Headers.Add("Prefer", $"outlook.timezone=\"{_mailboxTimeZone}\"");
        }, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (eventsResponse?.Value is null)
            throw new InvalidDataException("Graph returned no calendar collection.");

        if (Events.Count != eventsResponse.Value.Count
            || !Events.Zip(eventsResponse.Value).All(pair => SameEvent(pair.First, pair.Second)))
        {
            Events.Clear();
            foreach (Event ev in eventsResponse.Value)
                Events.Add(ev);
        }

        _lastEventsSync = DateTime.UtcNow;
        App.SetUpcomingEvents(Events);
        DebugText = DebugText.Insert(0, $"{DateTime.Now:t}: Events gotten\n");
    }

    private static bool SameEvent(Event first, Event second) =>
        first.Id == second.Id && first.Subject == second.Subject
        && first.Start?.DateTime == second.Start?.DateTime && first.Start?.TimeZone == second.Start?.TimeZone
        && first.End?.DateTime == second.End?.DateTime && first.End?.TimeZone == second.End?.TimeZone
        && first.IsAllDay == second.IsAllDay && first.IsOnlineMeeting == second.IsOnlineMeeting
        && first.OnlineMeetingUrl == second.OnlineMeetingUrl && first.WebLink == second.WebLink;

    public void OnNavigatedFrom()
    {
        UpdateItems();
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