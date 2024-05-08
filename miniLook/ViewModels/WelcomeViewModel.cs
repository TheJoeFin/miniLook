using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using miniLook.Contracts.Services;

namespace miniLook.ViewModels;

public partial class WelcomeViewModel : ObservableRecipient
{
    INavigationService NavigationService { get; }

    public WelcomeViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    [RelayCommand]
    public void GetSignedIn()
    {
        NavigationService.NavigateTo(typeof(ListDetailsViewModel).FullName!);
    }
}
