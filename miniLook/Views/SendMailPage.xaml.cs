using Microsoft.UI.Xaml.Controls;

using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class SendMailPage : Page
{
    public SendMailViewModel ViewModel
    {
        get;
    }

    public SendMailPage()
    {
        ViewModel = App.GetService<SendMailViewModel>();
        InitializeComponent();
    }
}
