using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph;
using Microsoft.Graph.Me.MailFolders.Item.Messages.Delta;
using Microsoft.Graph.Models;
using Microsoft.UI.Windowing;
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
    private bool isOverlayMode = false;

    public ObservableCollection<MailData> MailItems { get; private set; } = [];

    public ObservableCollection<ConversationGroup> ConversationGroups { get; private set; } = [];

    public ObservableCollection<Event> Events { get; private set; } = [];

    private GraphServiceClient? _graphClient;

    public bool HasGraphClient => _graphClient is not null;

    private string? deltaLink = null;
    private bool isSyncingMail = false;
    private bool isSigningOut = false;

    [ObservableProperty]
    private string debugText = $"{DateTime.Now.ToShortDateString()} debug text begins";

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

    private void MailItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateItems();
    }

    public void UpdateItems()
    {
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
        if (!IsLoadingContent)
            RunBackgroundSync();
    }

    public void RebuildConversationGroups()
    {
        var expandedConversations = ConversationGroups
            .Where(g => g.IsExpanded)
            .Select(g => g.ConversationId)
            .ToHashSet();

        ConversationGroups.Clear();

        var groups = MailItems
            .GroupBy(m => m.ConversationId)
            .Select(g => new ConversationGroup(g.Key, g))
            .OrderByDescending(g => g.ReceivedDateTime);

        foreach (var group in groups)
        {
            if (expandedConversations.Contains(group.ConversationId))
                group.IsExpanded = true;

            ConversationGroups.Add(group);
        }
    }

    public async void RunBackgroundSync()
    {
        HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;

        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Check new timer tick\n");
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

        IsLoadingContent = false;
    }

    public async void OnNavigatedTo(object parameter)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Navigated to ListView Detail Page\n");
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

    private async Task ClearOutContents()
    {
        MailItems.Clear();
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
    private void ToggleOverlayMode()
    {
        IsOverlayMode = !IsOverlayMode;

        if (IsOverlayMode)
            App.MainWindow.SetWindowPresenter(AppWindowPresenterKind.CompactOverlay);
        else
            App.MainWindow.SetWindowPresenter(AppWindowPresenterKind.Default);
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

        foreach (var message in group.Messages)
            MarkMessageIsReadAs(message, true);
    }

    public async Task ArchiveThisMailItem(MailData listDetailsMenuItem)
    {
        if (_graphClient is null)
            return;

        var foldersResponse = await _graphClient.Me
            .MailFolders
            .GetAsync(config =>
            {
                config.QueryParameters.Filter = "displayName eq 'Archive'";
            });

        MailFolder? archiveFolder = foldersResponse?.Value?.FirstOrDefault();

        if (archiveFolder is null)
            return;

        List<MailData> allOfConversation = MailItems.Where(m => m.ConversationId == listDetailsMenuItem.ConversationId).ToList();

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

        _ = _graphClient.Me
            .MailFolders["Inbox"]
            .Messages[listDetailsMenuItem.Id]
            .PatchAsync(new Message { IsRead = isRead });
    }

    public async Task DeleteThisMailItem(MailData listDetailsMenuItem)
    {
        if (_graphClient is null)
            return;

        var foldersResponse = await _graphClient.Me
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

    private async Task TryToLoadMail(MailData? selectMailData = null)
    {
        if (loadedMail)
            return;

        loadedMail = true;

        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Trying to load mail\n");
        if (!GraphService.IsAuthenticated || GraphService.Client is null)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Not authenticated, triggering sign-in\n");
            loadedMail = false;
            await GraphService.SignInAsync();
            return;
        }

        IsLoadingContent = true;

        await MailCacheService.InitializeAsync();

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
        var me = await _graphClient.Me.GetAsync();
        AccountName = me?.DisplayName ?? string.Empty;

        await GetEvents();
        await SyncMail();

        IsLoadingContent = false;
        await MailCacheService.SaveEmailsAsync(MailItems);
    }

    private async Task SyncMail()
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Syncing Mail\n");
        if (_graphClient is null
            || !HasInternet
            || isSigningOut
            || !GraphService.IsAuthenticated
            || isSyncingMail)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Graph client is null {_graphClient is null} or isSyncingMail {isSyncingMail} caused return\n");
            return;
        }

        isSyncingMail = true;

        DeltaGetResponse? currentPage;

        if (deltaLink is not null)
        {
            currentPage = await new DeltaRequestBuilder(deltaLink, _graphClient.RequestAdapter).GetAsDeltaGetResponseAsync();
        }
        else
        {
            try
            {
                currentPage = await _graphClient.Me.MailFolders["Inbox"].Messages.Delta.GetAsDeltaGetResponseAsync();
            }
            catch (Exception)
            {
                isSyncingMail = false;
                return;
            }
        }

        do
        {
            if (currentPage?.Value is null)
                break;

            foreach (Message message in currentPage.Value)
            {
                if (isSigningOut || !HasInternet)
                {
                    isSyncingMail = false;
                    return;
                }

                MailData? matchingMessage = MailItems.FirstOrDefault(m => m.Id == message.Id);
                MailData newMail = new(message);
                if (message.AdditionalData is not null
                    && message.AdditionalData.TryGetValue("@removed", out object? removed))
                {
                    if (matchingMessage is not null)
                        MailItems.Remove(matchingMessage);

                    continue;
                }

                if (message.AdditionalData is null && message.IsRead is not null)
                {
                    if (matchingMessage is not null)
                    {
                        int index = MailItems.IndexOf(matchingMessage);
                        matchingMessage.IsRead = (bool)message.IsRead;
                    }

                    continue;
                }

                if (message.OdataType == "#microsoft.graph.eventMessageRequest")
                {
                    newMail.IsEvent = true;
                }

                if (message.HasAttachments is true)
                {
                    var result = await _graphClient
                        .Me.Messages[message.Id]
                        .Attachments.GetAsync();

                    if (result?.Value is not null)
                    {
                        newMail.AttachmentsCount = result.Value.Count;

                        foreach (Attachment attachment in result.Value)
                        {
                            if (attachment is FileAttachment fileAttachment)
                                Debug.WriteLine("File attachment found: " + fileAttachment.Name);
                        }
                    }
                }

                if (MailItems.Count == 0)
                {
                    MailItems.Add(newMail);
                    continue;
                }

                if (matchingMessage is not null)
                    continue;

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
            }

            if (currentPage.OdataNextLink is not null)
                currentPage = await new DeltaRequestBuilder(currentPage.OdataNextLink, _graphClient.RequestAdapter).GetAsDeltaGetResponseAsync();
            else
                break;
        }
        while (currentPage is not null);

        if (currentPage?.OdataDeltaLink is not null)
        {
            deltaLink = currentPage.OdataDeltaLink;
            await MailCacheService.SaveDeltaLink(deltaLink);
            await MailCacheService.SaveEmailsAsync(MailItems);
        }

        isSyncingMail = false;
        LastSync = DateTime.Now;
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
        App.SetTaskbarBadgeToNumber(NumberUnread);
        RebuildConversationGroups();
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Mail synced\n");
    }

    private async Task GetEvents()
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Getting Events\n");
        if (isSigningOut
            || !HasInternet
            || _graphClient is null)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Graph client is null {_graphClient is null} caused return\n");
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime endOfWeek = now.AddDays(2);

        try
        {
            // Get the user's mailbox settings to determine their time zone
            var user = await _graphClient.Me.GetAsync(config =>
            {
                config.QueryParameters.Select = ["mailboxSettings"];
            });

            string timeZone = user?.MailboxSettings?.TimeZone ?? "UTC";

            var eventsResponse = await _graphClient.Me.CalendarView.GetAsync(config =>
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
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Events gotten\n");
    }

    public void OnNavigatedFrom()
    {
        App.SetTaskbarBadgeToNumber(NumberUnread);
    }

    private async void OnAuthenticationStateChanged(object? sender, bool isSignedIn)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Auth state changed to {isSignedIn}\n");
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
