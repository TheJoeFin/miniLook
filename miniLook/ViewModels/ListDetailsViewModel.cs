using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graph;
using miniLook.Contracts.ViewModels;
using miniLook.Core.Contracts.Services;
using miniLook.Core.Models;
using System.Collections.ObjectModel;

namespace miniLook.ViewModels;

public partial class ListDetailsViewModel : ObservableRecipient, INavigationAware
{
    private bool loadedMail = false;

    private readonly ISampleDataService _sampleDataService;

    [ObservableProperty]
    private SampleOrder? selected;

    public ObservableCollection<SampleOrder> SampleItems { get; private set; } = [];

    public ListDetailsViewModel(ISampleDataService sampleDataService)
    {
        _sampleDataService = sampleDataService;
        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;
    }

    public async void OnNavigatedTo(object parameter)
    {
        SampleItems.Clear();

        // TODO: Replace with real data.
        var data = await _sampleDataService.GetListDetailsDataAsync();

        foreach (var item in data)
        {
            SampleItems.Add(item);
        }

        EstablishGraph();
    }

    private async void TryToLoadMail()
    {
        loadedMail = true;
        IProvider? provider = ProviderManager.Instance.GlobalProvider;
        if (provider is not null && provider?.State != ProviderState.SignedIn)
        {
            // Prompt for authentication.
            await provider?.SignInAsync();
        }

        GraphServiceClient graphClient = provider.GetClient();
        IMailFolderMessagesCollectionPage messages = await graphClient.Me.MailFolders.Inbox.Messages.Request().GetAsync();

        foreach (Message message in messages)
        {
            string newText = message.IsRead is true ? "" : "🆕 | ";
            SampleItems.Add(new SampleOrder
            {
                Company = $"{newText}{message.Subject }",
                Status = message.From.EmailAddress.Address,
                
                Details = [
                    new SampleOrderDetail { 
                        CategoryDescription = message.BodyPreview,
                        ProductName = message.From.EmailAddress.Address },
                    ]
            });
        }
    }

    public void OnNavigatedFrom()
    {
    }

    public void EnsureItemSelected()
    {
        Selected ??= SampleItems.First();
    }


    private static void EstablishGraph()
    {
        string clientId = Environment.GetEnvironmentVariable("miniLookId", EnvironmentVariableTarget.User) ?? string.Empty;
        string[] scopes = ["User.Read", "mail.read"];

        ProviderManager.Instance.GlobalProvider = new MsalProvider(clientId, scopes);
    }


    private async void OnProviderStateChanged(object? sender, ProviderStateChangedEventArgs args)
    {
        if (args.NewState == ProviderState.Loading || ProviderManager.Instance.GlobalProvider is not IProvider provider)
            return;

//         bool silentSuccess = await provider?.TrySilentSignInAsync();
        if (provider?.State == ProviderState.SignedOut)
                await provider?.SignInAsync();

        GraphServiceClient graphClient = provider.GetClient();

        if (!loadedMail && provider?.State == ProviderState.SignedIn)
            TryToLoadMail();
    }
}
