using Microsoft.UI.Xaml.Controls;
using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel { get; }

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);

        ViewModel.NavigationService.Frame = NavigationFrame;
    }
}
