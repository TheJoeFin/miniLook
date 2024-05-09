using CommunityToolkit.Authentication;
using CommunityToolkit.Authentication.Extensions;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Windows.Storage;

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

    public ObservableCollection<MailData> MailItems { get; private set; } = [];

    public ObservableCollection<Event> Events { get; private set; } = [];

    public DispatcherTimer checkTimer = new();
    private GraphServiceClient? _graphClient;

    private object? deltaLink = null;
    private IMessageDeltaCollectionPage? previousPage = null;
    private bool isSyncingMail = false;

    [ObservableProperty]
    private string debugText = $"debug text\n{DateTime.Now.ToShortDateString()}\n{DateTime.Now.ToShortTimeString()}";

    private INavigationService NavigationService { get; }

    private IMailCacheService MailCacheService { get; }

    private IGraphService GraphService { get; }

    public ListDetailsViewModel(INavigationService navigationService, IMailCacheService mailCacheService, IGraphService graphService)
    {
        NavigationService = navigationService;
        MailCacheService = mailCacheService;
        GraphService = graphService;
    }

    private void MailItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
    }

    private async void CheckTimer_Tick(object? sender, object e)
    {
        DebugText += $"\n{DateTime.Now.ToShortTimeString()}: Check new timer tick";
        checkTimer.Stop();
        if (_graphClient is null)
        {
            DebugText += $"\nGraph Client is null, returning";
            return;
        }

        Debug.WriteLine("Checking for new mail");
        IsLoadingContent = true;
        await GetEvents();
        await SyncMail();

        IsLoadingContent = false;
        checkTimer.Start();
    }

    public async void OnNavigatedTo(object parameter)
    {
        DebugText += $"\nNavigated to ListView Detail Page";
        MailItems.Clear();
        Events.Clear();

        ProviderManager.Instance.ProviderStateChanged -= OnProviderStateChanged;
        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;

        MailItems.CollectionChanged -= MailItems_CollectionChanged;
        MailItems.CollectionChanged += MailItems_CollectionChanged;

        checkTimer.Interval = TimeSpan.FromSeconds(10);
        checkTimer.Tick += CheckTimer_Tick;

        await EstablishGraph();
        await TryToLoadMail();
    }

    [RelayCommand]
    private static void GoToOutlook()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://outlook.live.com/mail/0/"));
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await ClearOutContents();
        await TryToLoadMail();
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
        await ClearOutContents();
        await ProviderManager.Instance.GlobalProvider?.SignOutAsync();
        NavigationService.NavigateTo(typeof(WelcomeViewModel).FullName!);
    }

    [RelayCommand]
    private async Task SignIn()
    {
        ProviderManager.Instance.GlobalProvider = null;
        await EstablishGraph();
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

        DebugText += $"\nTrying to load mail";
        if (ProviderManager.Instance.GlobalProvider is not IProvider provider)
        {
            DebugText += $"\nProvider is not provider, returning";
            return;
        }

        _graphClient = provider.GetClient();

        IsLoadingContent = true;

        await MailCacheService.InitializeAsync();

        IEnumerable<MailData> tempMailItems = await MailCacheService.GetEmailsAsync();

        foreach (MailData mail in tempMailItems)
            MailItems.Add(mail);

        deltaLink = MailCacheService.DeltaLink;

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
        DebugText += $"\nSyncing Mail";
        if (_graphClient is null || isSyncingMail)
        {
            DebugText += $"\nGraph client is null {_graphClient is null} or isSyncingMail {isSyncingMail} caused return";
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
            currentPageOfMessages = await _graphClient.Me.MailFolders.Inbox.Messages
            .Delta()
            .Request()
            .GetAsync();
        }

        do
        {
            foreach (Message message in currentPageOfMessages)
            {
                MailData newMail = new(message);
                if (message.AdditionalData is not null 
                    && message.AdditionalData.TryGetValue("@removed", out object? removed))
                {
                    MailData? matchingMessage = MailItems.FirstOrDefault(m => m.Id == message.Id);
                    if (matchingMessage is not null)
                        MailItems.Remove(matchingMessage);

                    continue;
                }

                if (message.AdditionalData is null && message.IsRead is not null)
                {
                    MailData? changedMessage = MailItems.FirstOrDefault(m => m.Id == message.Id);
                    if (changedMessage is not null)
                    {
                        int index = MailItems.IndexOf(changedMessage);
                        changedMessage.IsRead = (bool)message.IsRead;
                    }

                    continue;
                }

                if (MailItems.Count == 0)
                {
                    MailItems.Add(newMail);
                    continue;
                }

                if (MailItems.First().ReceivedDateTime < message.ReceivedDateTime)
                    MailItems.Insert(0, newMail);
                else
                    MailItems.Add(newMail);

                // TODO find the right spot to insert a new mail item
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
        }

        isSyncingMail = false;
        LastSync = DateTime.Now;
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
        DebugText += $"\nMail synced at {LastSync:t}";
    }

    private async Task GetEvents()
    {
        DebugText += $"\nGetting Events";
        if (_graphClient is null)
        {
            DebugText += $"\nGraph client is null, returning";
            return;
        }

        // Get the user's mailbox settings to determine
        // their time zone
        User user = await _graphClient.Me.Request()
            .Select(u => new { u.MailboxSettings })
            .GetAsync();

        DateTime now = DateTime.UtcNow;
        DateTime endOfWeek = now.AddDays(2);

        List<QueryOption> queryOptions =
        [
            new QueryOption("startDateTime", now.ToString("o")),
            new QueryOption("endDateTime", endOfWeek.ToString("o"))
        ];

        IUserCalendarViewCollectionPage events = await _graphClient.Me.CalendarView.Request(queryOptions)
                .Header("Prefer", $"outlook.timezone=\"{user.MailboxSettings.TimeZone}\"")
                .OrderBy("start/dateTime")
                .Top(3)
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

        DebugText += $"\nEvents gotten";
    }

    public void OnNavigatedFrom()
    {
        loadedMail = false;
    }

    private async Task EstablishGraph()
    {
        DebugText += $"\nEstablishing Graph";
        if (ProviderManager.Instance.GlobalProvider != null)
        {
            DebugText += $"\nGlobal Provider not null";
            return;
        }
    }

    private async void OnProviderStateChanged(object? sender, ProviderStateChangedEventArgs args)
    {
        DebugText += $"\nProvider state changed to {args.NewState}";
        if (args.NewState != ProviderState.SignedIn || ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        if (!loadedMail && provider?.State == ProviderState.SignedIn)
            await TryToLoadMail();
    }
}
