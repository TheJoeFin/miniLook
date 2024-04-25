using CommunityToolkit.Authentication;
using CommunityToolkit.Authentication.Extensions;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.UI.Xaml;
using miniLook.Contracts.ViewModels;
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

    public ObservableCollection<Message> MailItems { get; private set; } = [];

    public ObservableCollection<Event> Events { get; private set; } = [];

    public DispatcherTimer checkTimer = new();
    private GraphServiceClient? _graphClient;
    private DateTimeOffset lastSync = DateTimeOffset.MinValue;

    public ListDetailsViewModel()
    {
        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;

        MailItems.CollectionChanged += MailItems_CollectionChanged;

        checkTimer.Interval = TimeSpan.FromSeconds(10);
        checkTimer.Tick += CheckTimer_Tick;
        checkTimer.Start();
    }

    private void MailItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NumberUnread = MailItems.Where(MailItems => MailItems.IsRead == false).Count();
    }

    private async void CheckTimer_Tick(object? sender, object e)
    {
        checkTimer.Stop();
        if (_graphClient is null)
            return;

        Debug.WriteLine("Checking for new mail");

        string filter = $"receivedDateTime gt {lastSync:yyyy-MM-ddTHH:mm:ssZ}";

        IMessageDeltaCollectionPage messages = await _graphClient.Me.MailFolders.Inbox.Messages
            .Delta()
            .Request()
            .Filter(filter)
            .GetAsync();

        foreach (Message newMessage in messages)
            MailItems.Insert(0, newMessage);

        List<Message> messagesToDelete = [];

        // this method seems to be a bit intensive and I should figure out how to do it better
        // not sure how the details work, but I'll keep trying to figure that out
        foreach (Message message in MailItems)
        {
            Message? refreshMessage = null;
            try
            {
                refreshMessage = await _graphClient.Me.MailFolders.Inbox.Messages[message.Id]
                    .Request()
                    .GetAsync();
            }
            catch (ServiceException)
            {
                // message not found and can be removed
                messagesToDelete.Add(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            if (refreshMessage is null)
                continue;

            message.IsRead = refreshMessage.IsRead;
        }

        foreach (Message message in messagesToDelete)
            MailItems.Remove(message);

        lastSync = DateTimeOffset.UtcNow;

        await GetEvents();

        checkTimer.Start();
    }

    public async void OnNavigatedTo(object parameter)
    {
        MailItems.Clear();
        Events.Clear();
        await EstablishGraph();
    }

    [RelayCommand]
    private void GoToOutlook()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://outlook.live.com/mail/0/"));
    }

    [RelayCommand]
    private void Refresh()
    {
        MailItems.Clear();
        Events.Clear();
        TryToLoadMail();
    }

    private async void TryToLoadMail()
    {
        loadedMail = true;

        if (ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        _graphClient = provider.GetClient();

        User me = await _graphClient.Me.Request().GetAsync();
        AccountName = me.DisplayName;

        IMailFolderMessagesCollectionPage messages = await _graphClient.Me.MailFolders.Inbox.Messages
            .Request()
            .Top(100)
            .GetAsync();

        foreach (Message message in messages)
            MailItems.Add(message);

        await GetEvents();

        lastSync = DateTimeOffset.UtcNow;
    }

    private async Task GetEvents()
    {
        if (_graphClient is null)
            return;

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
    }

    public void OnNavigatedFrom()
    {
    }


    private static async Task EstablishGraph()
    {
        if (ProviderManager.Instance.GlobalProvider != null)
            return;

        string clientId = Environment.GetEnvironmentVariable("miniLookId", EnvironmentVariableTarget.User) ?? string.Empty;
        string[] scopes = ["User.Read", "Mail.ReadWrite", "offline_access", "Calendars.Read", "MailboxSettings.Read"];


        MsalProvider provider = new(clientId, scopes, null, false, true);

        string cacheFileName = "msal_cache.dat";
        string cacheDirectory = ApplicationData.Current.LocalCacheFolder.Path;
        // Configure the token cache storage for non-UWP applications.
        // https://github.com/AzureAD/microsoft-authentication-extensions-for-dotnet/wiki/Cross-platform-Token-Cache
        // https://github.com/Richasy/Graph-Controls/tree/main/Samples/ManualGraphRequestSample
        StorageCreationProperties storageProperties = new StorageCreationPropertiesBuilder(
            cacheFileName, cacheDirectory).Build();
        await provider.InitTokenCacheAsync(storageProperties);

        ProviderManager.Instance.GlobalProvider = provider;

        bool silentSuccess = await provider.TrySilentSignInAsync();

        if (!silentSuccess)
            await provider.SignInAsync();
    }

    private void OnProviderStateChanged(object? sender, ProviderStateChangedEventArgs args)
    {
        if (args.NewState != ProviderState.SignedIn || ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        if (!loadedMail && provider?.State == ProviderState.SignedIn)
            TryToLoadMail();
    }
}
