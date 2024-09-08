using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using Microsoft.UI.Xaml;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.WinUI.Helpers;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;


namespace miniLook.ViewModels;

public partial class ListDetailsViewModel : ObservableRecipient, INavigationAware
{
    private bool loadedMail = false;

    [ObservableProperty]
    private Message? selected;

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

    public ObservableCollection<MailData> MailItems { get; private set; } = [];

    public ObservableCollection<Event> Events { get; private set; } = [];

    public DispatcherTimer checkTimer = new();
    private GraphServiceClient? _graphClient;

    private object? deltaLink = null;
    private IMessageDeltaCollectionPage? previousPage = null;
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
            CheckTimer_Tick(null, null);
    }

    private async void CheckTimer_Tick(object? sender, object? e)
    {
        HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;

        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Check new timer tick\n");
        checkTimer.Stop();
        if (_graphClient is null)
        {
            DebugText = DebugText.Insert(0, $"Graph Client is null, returning\n");
            checkTimer.Start();

            if (HasInternet)
                _graphClient = ProviderManager.Instance.GlobalProvider?.GetClient();

            return;
        }

        Debug.WriteLine("Checking for new mail");
        DebugText = DebugText.Insert(0, $"Checking for new mail\n");
        IsLoadingContent = true;
        await GetEvents();
        await SyncMail();

        IsLoadingContent = false;
        checkTimer.Start();
    }

    private void setBadgeNumber(int num)
    {
        // Get the blank badge XML payload for a badge number
        XmlDocument badgeXml =
            BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

        // Set the value of the badge in the XML to our number

        if (badgeXml.SelectSingleNode("/badge") is not XmlElement badgeElement)
            return;

        badgeElement.SetAttribute("value", num.ToString());

        // Create the badge notification
        BadgeNotification badge = new BadgeNotification(badgeXml);

        // Create the badge updater for the application
        BadgeUpdater badgeUpdater =
            BadgeUpdateManager.CreateBadgeUpdaterForApplication();

        // And update the badge
        badgeUpdater.Update(badge);

    }

    public async void OnNavigatedTo(object parameter)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Navigated to ListView Detail Page\n");
        isSigningOut = false;
        MailItems.Clear();
        Events.Clear();

        HasInternet = NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable;

        ProviderManager.Instance.ProviderStateChanged -= OnProviderStateChanged;
        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;

        MailItems.CollectionChanged -= MailItems_CollectionChanged;
        MailItems.CollectionChanged += MailItems_CollectionChanged;

        checkTimer.Interval = TimeSpan.FromSeconds(10);
        checkTimer.Tick += CheckTimer_Tick;

        await TryToLoadMail();
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
            CheckTimer_Tick(null, null);
    }

    private async Task ClearOutContents()
    {
        MailItems.Clear();
        Events.Clear();

        checkTimer.Stop();

        deltaLink = null;
        previousPage = null;

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
        await ProviderManager.Instance.GlobalProvider?.SignOutAsync();
        NavigationService.NavigateTo(typeof(WelcomeViewModel).FullName!);
    }

    [RelayCommand]
    private async Task SignIn()
    {
        ProviderManager.Instance.GlobalProvider = null;
        await GraphService.SignInAsync();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
    }

    private async Task TryToLoadMail()
    {
        if (loadedMail)
            return;

        loadedMail = true;

        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Trying to load mail\n");
        if (ProviderManager.Instance.GlobalProvider is not IProvider provider)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Provider is not provider, returning\n");
            return;
        }

        IsLoadingContent = true;

        await MailCacheService.InitializeAsync();

        IEnumerable<MailData> tempMailItems = await MailCacheService.GetEmailsAsync();

        foreach (MailData mail in tempMailItems)
            MailItems.Add(mail);

        deltaLink = MailCacheService.DeltaLink;

        if (!HasInternet)
        {
            checkTimer.Start();
            IsLoadingContent = false;
            return;
        }

        _graphClient = provider.GetClient();
        User me = await _graphClient.Me.Request().GetAsync();
        AccountName = me.DisplayName;

        await GetEvents();
        await SyncMail();

        IsLoadingContent = false;
        await MailCacheService.SaveEmailsAsync(MailItems);

        checkTimer.Start();
    }

    private async Task SyncMail()
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Syncing Mail\n");
        if (_graphClient is null
            || !HasInternet
            || isSigningOut
            || ProviderManager.Instance.GlobalProvider is not IProvider provider
            || isSyncingMail)
        {
            DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Graph client is null {_graphClient is null} or isSyncingMail {isSyncingMail} caused return\n");
            return;
        }

        isSyncingMail = true;

        IMessageDeltaCollectionPage currentPageOfMessages;

        if (deltaLink is not null)
        {
            previousPage ??= new MessageDeltaCollectionPage();

            previousPage.InitializeNextPageRequest(_graphClient, deltaLink.ToString());
            currentPageOfMessages = await previousPage.NextPageRequest.GetAsync();
        }
        else
        {
            try
            {
                currentPageOfMessages = await _graphClient.Me.MailFolders.Inbox.Messages
                    .Delta()
                    .Request()
                    .GetAsync();
            }
            catch (Exception)
            {
                return;
            }
        }

        do
        {
            foreach (Message message in currentPageOfMessages)
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

            previousPage = currentPageOfMessages;
        }
        while (currentPageOfMessages.NextPageRequest is not null
        && (currentPageOfMessages = await currentPageOfMessages.NextPageRequest.GetAsync()) is not null);

        object? outDeltaLink = null;
        bool successInGettingDeltaLink = currentPageOfMessages?.AdditionalData.TryGetValue("@odata.deltaLink", out outDeltaLink) is true;

        if (successInGettingDeltaLink)
        {
            deltaLink = outDeltaLink;
            await MailCacheService.SaveDeltaLink(deltaLink?.ToString());
            await MailCacheService.SaveEmailsAsync(MailItems);
        }

        isSyncingMail = false;
        LastSync = DateTime.Now;
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
        setBadgeNumber(NumberUnread);
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

        List<QueryOption> queryOptions =
        [
            new QueryOption("startDateTime", now.ToString("o")),
            new QueryOption("endDateTime", endOfWeek.ToString("o"))
        ];

        try
        {
            // Get the user's mailbox settings to determine
            // their time zone
            User user = await _graphClient.Me.Request()
                .Select(u => new { u.MailboxSettings })
                .GetAsync();

            IUserCalendarViewCollectionPage events = await _graphClient.Me.CalendarView.Request(queryOptions)
                    .Header("Prefer", $"outlook.timezone=\"{user.MailboxSettings.TimeZone}\"")
                    .OrderBy("start/dateTime")
                    .Top(4)
                    .GetAsync();

            // check to see if any events are different:
            bool eventsChanged = false;
            for (int i = 0; i < events.Count; i++)
            {
                if (Events.Count <= i || events[i].Id != Events[i].Id)
                {
                    eventsChanged = true;
                    break;
                }
            }

            if (!eventsChanged)
                return;

            Events.Clear();

            foreach (Event ev in events)
                Events.Add(ev);
        }
        catch (Exception)
        {
            return;
        }

        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Events gotten\n");
    }

    public void OnNavigatedFrom()
    {
        loadedMail = false;
    }

    private async void OnProviderStateChanged(object? sender, ProviderStateChangedEventArgs args)
    {
        DebugText = DebugText.Insert(0, $"{DateTime.Now.ToShortTimeString()}: Provider state changed to {args.NewState}\n");
        if (args.NewState != ProviderState.SignedIn || ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        if (!loadedMail && provider?.State == ProviderState.SignedIn)
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
