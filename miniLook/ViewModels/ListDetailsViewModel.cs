using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graph;
using miniLook.Contracts.ViewModels;
using miniLook.Core.Contracts.Services;
using miniLook.Core.Models;

namespace miniLook.ViewModels;

public partial class ListDetailsViewModel : ObservableRecipient, INavigationAware
{
    private readonly ISampleDataService _sampleDataService;

    [ObservableProperty]
    private SampleOrder? selected;

    public ObservableCollection<SampleOrder> SampleItems { get; private set; } = new ObservableCollection<SampleOrder>();

    public ListDetailsViewModel(ISampleDataService sampleDataService)
    {
        _sampleDataService = sampleDataService;
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
    }

    public void OnNavigatedFrom()
    {
    }

    public void EnsureItemSelected()
    {
        Selected ??= SampleItems.First();
    }


    //// User auth token credential
    //private static DeviceCodeCredential? _deviceCodeCredential;
    //// Client configured with user authentication
    //private static GraphServiceClient? _userClient;

    //public static void InitializeGraphForUserAuth(Func<DeviceCodeInfo, CancellationToken, Task> deviceCodePrompt)
    //{
    //    var options = new DeviceCodeCredentialOptions
    //    {
    //        ClientId = Environment.GetEnvironmentVariable("ClientId"),
    //        TenantId = "common",
    //        DeviceCodeCallback = deviceCodePrompt,
    //    };

    //    string[] graphUserScopes = ["User.Read", "Mail.Read", "Mail.Send", "Calendars.read"];

    //    _deviceCodeCredential = new DeviceCodeCredential(options);

    //    _userClient = new GraphServiceClient(_deviceCodeCredential, graphUserScopes);
    //}
}
