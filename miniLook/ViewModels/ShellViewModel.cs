using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Navigation;
using miniLook.Contracts.Services;

namespace miniLook.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    [ObservableProperty]
    private bool isBackEnabled;

    [ObservableProperty]
    private object? selected;

    // TODO: get the account name if logged in
    [ObservableProperty]
    private string accountName = string.Empty;

    [RelayCommand]
    private void Back()
    {
        if (NavigationService.CanGoBack)
            NavigationService.GoBack();
    }

    public INavigationService NavigationService
    {
        get;
    }

    public ShellViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        IsBackEnabled = NavigationService.CanGoBack;
    }
}
