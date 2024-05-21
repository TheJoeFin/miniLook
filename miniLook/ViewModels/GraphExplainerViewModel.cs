using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using miniLook.Contracts.Services;
using System.Diagnostics;
using Windows.System;

namespace miniLook.ViewModels;

public partial class GraphExplainerViewModel : ObservableRecipient
{
    INavigationService NavigationService { get; }


    public GraphExplainerViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    [RelayCommand]
    public void GetSignedIn()
    {
        NavigationService.NavigateTo(typeof(ListDetailsViewModel).FullName!);
    }

    [RelayCommand]
    public async Task EmailMe()
    {
        _ = await Launcher.LaunchUriAsync(new Uri(string.Format("mailto:joe@joefinapps.com")));
    }
}
