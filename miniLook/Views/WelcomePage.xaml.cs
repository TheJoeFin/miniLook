using Microsoft.UI.Xaml.Controls;

using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class WelcomePage : Page
{
    public WelcomeViewModel ViewModel
    {
        get;
    }

    public WelcomePage()
    {
        ViewModel = App.GetService<WelcomeViewModel>();
        InitializeComponent();
    }
}
