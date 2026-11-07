using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

using miniLook.ViewModels;

namespace miniLook.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    private async void BackgroundUpdatesToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (sender is ToggleSwitch toggle)
        {
            toggle.IsEnabled = false;
            await ViewModel.SetBackgroundTrayUpdatesAsync(toggle.IsOn);
            toggle.IsEnabled = true;
        }
    }
}