using CommunityToolkit.WinUI.UI.Controls;

using Microsoft.UI.Xaml.Controls;

using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class ListDetailsPage : Page
{
    public ListDetailsViewModel ViewModel
    {
        get;
    }

    public ListDetailsPage()
    {
        ViewModel = App.GetService<ListDetailsViewModel>();
        InitializeComponent();
    }

    private void OnViewStateChanged(object sender, ListDetailsViewState e)
    {
    }

    private void ArchiveSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {

    }

    private void ReadSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {

    }
}
