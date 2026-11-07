using Microsoft.UI.Xaml.Controls;
using miniLook.Contracts.Services;
using miniLook.Helpers;
using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class ShellPage : Page, ITrayFlyoutPage
{
    public ShellViewModel ViewModel { get; }

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
    }

    public void OnShown()
    {
        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.OnShown();

        string pageKey = App.GetService<IGraphService>().IsAuthenticated
            ? typeof(ListDetailsViewModel).FullName!
            : typeof(WelcomeViewModel).FullName!;
        ViewModel.NavigationService.NavigateTo(pageKey);
    }

    public void OnHidden()
    {
        ViewModel.OnHidden();
        if (NavigationFrame.GetPageViewModel() is ListDetailsViewModel listDetailsViewModel)
            listDetailsViewModel.OnNavigatedFrom();
    }

    public void OnClosed()
    {
        ViewModel.OnHidden();
        if (NavigationFrame.GetPageViewModel() is miniLook.Contracts.ViewModels.INavigationAware page)
            page.OnNavigatedFrom();
        if (ReferenceEquals(ViewModel.NavigationService.Frame, NavigationFrame))
            ViewModel.NavigationService.Frame = null;
        NavigationFrame.Content = null;
        NavigationFrame.BackStack.Clear();
    }
}