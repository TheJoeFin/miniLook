using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graph;
using Microsoft.UI.Xaml;
using miniLook.Contracts.ViewModels;
using miniLook.Core.Contracts.Services;
using System.Collections.ObjectModel;

namespace miniLook.ViewModels;

public partial class ListDetailsViewModel : ObservableRecipient, INavigationAware
{
    private bool loadedMail = false;

    private readonly ISampleDataService _sampleDataService;

    [ObservableProperty]
    private Message? selected;

    public ObservableCollection<Message> SampleItems { get; private set; } = [];

    public DispatcherTimer checkTimer = new();
    private GraphServiceClient _graphClient;

    public ListDetailsViewModel(ISampleDataService sampleDataService)
    {
        _sampleDataService = sampleDataService;
        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;

        checkTimer.Interval = TimeSpan.FromSeconds(5);
        checkTimer.Tick += CheckTimer_Tick;
    }

    private void CheckTimer_Tick(object? sender, object e)
    {
        throw new NotImplementedException();
    }

    public async void OnNavigatedTo(object parameter)
    {
        SampleItems.Clear();
        await EstablishGraph();
    }

    private async void TryToLoadMail()
    {
        loadedMail = true;

        if (ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

        _graphClient = provider.GetClient();
        IMailFolderMessagesCollectionPage messages = await _graphClient.Me.MailFolders.Inbox.Messages.Request().GetAsync();

        foreach (Message message in messages)
            SampleItems.Add(message);
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
