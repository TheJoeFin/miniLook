using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;

namespace miniLook.ViewModels;

public partial class MailDetailViewModel : ObservableRecipient, INavigationAware
{
    [ObservableProperty]
    private MailData? mailItem;

    private INavigationService NavigationService { get; }

    private IMailCacheService MailCacheService { get; }

    private IGraphService GraphService { get; }

    public MailDetailViewModel(INavigationService navigationService, IMailCacheService mailCacheService, IGraphService graphService)
    {
        NavigationService = navigationService;
        MailCacheService = mailCacheService;
        GraphService = graphService;
    }

    public void OnNavigatedTo(object parameter)
    {
        if (parameter is MailData mailData)
        {
            MailItem = mailData;
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void GoBack()
    {
        if (NavigationService.CanGoBack)
            NavigationService.GoBack();
    }
}
