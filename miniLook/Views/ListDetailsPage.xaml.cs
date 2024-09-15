using CommunityToolkit.WinUI.UI.Controls;

using Microsoft.UI.Xaml.Controls;
using miniLook.Models;
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

    private async void ArchiveSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (args.SwipeControl.DataContext is not MailData swipedItem)
            return;

        await ViewModel.ArchiveThisMailItem(swipedItem);
    }

    private void ReadSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (args.SwipeControl.DataContext is not MailData swipedItem)
            return;

        ViewModel.MarkMessageIsReadAs(swipedItem, true);
    }
}
