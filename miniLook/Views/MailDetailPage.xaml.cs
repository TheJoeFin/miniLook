using Microsoft.UI.Xaml.Controls;
using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class MailDetailPage : Page
{
    public MailDetailViewModel ViewModel { get; }

    public MailDetailPage()
    {
        ViewModel = App.GetService<MailDetailViewModel>();
        InitializeComponent();
    }
}
