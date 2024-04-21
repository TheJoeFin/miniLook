using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using Microsoft.UI.Xaml;
using miniLook.Contracts.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace miniLook.ViewModels;

public partial class ListDetailsViewModel : ObservableRecipient, INavigationAware
{
    private bool loadedMail = false;

    [ObservableProperty]
    private Message? selected;

    [ObservableProperty]
    private string accountName = string.Empty;

    public ObservableCollection<Message> SampleItems { get; private set; } = [];

    public DispatcherTimer checkTimer = new();
    private GraphServiceClient? _graphClient;
    private DateTimeOffset lastSync = DateTimeOffset.MinValue;

    public ListDetailsViewModel()
    {
        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;

        checkTimer.Interval = TimeSpan.FromSeconds(10);
        checkTimer.Tick += CheckTimer_Tick;
        checkTimer.Start();
    }

    private async void CheckTimer_Tick(object? sender, object e)
    {
        if (_graphClient is null)
            return;

        Debug.WriteLine("Checking for new mail");

        string filter = $"receivedDateTime gt {lastSync:yyyy-MM-ddTHH:mm:ssZ}";

        IMailFolderMessagesCollectionPage messages = await _graphClient.Me.MailFolders.Inbox.Messages
            .Request()
            .Filter(filter)
            .GetAsync();

        if (messages.Count == 0)
            return;

        foreach (Message message in messages)
            SampleItems.Insert(0, message);

        lastSync = DateTimeOffset.UtcNow;
    }

    public async void OnNavigatedTo(object parameter)
    {
        SampleItems.Clear();
        await EstablishGraph();
    }

    [RelayCommand]
    private void GoToOutlook()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://outlook.live.com/mail/0/"));
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
            SampleItems.Add(message);

        lastSync = DateTimeOffset.UtcNow;
    }

    public void OnNavigatedFrom()
    {
    }


    private static async Task EstablishGraph()
    {
        string clientId = Environment.GetEnvironmentVariable("miniLookId", EnvironmentVariableTarget.User) ?? string.Empty;
        string[] scopes = ["User.Read", "Mail.ReadWrite", "offline_access"];

        ProviderManager.Instance.GlobalProvider = new MsalProvider(clientId, scopes);

        if (ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        bool silentSuccess = await provider.TrySilentSignInAsync();

        if (provider.State == ProviderState.SignedOut && !silentSuccess)
        {
            await provider.SignInAsync();
        }
    }


    private void OnProviderStateChanged(object? sender, ProviderStateChangedEventArgs args)
    {
        if (args.NewState != ProviderState.SignedIn || ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        if (!loadedMail && provider?.State == ProviderState.SignedIn)
            TryToLoadMail();
    }
}
