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
        Loaded += ListDetailsPage_Loaded;
    }

    private void ListDetailsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        bool isInPopOut = App.PopOutWindow is not null
            && XamlRoot == App.PopOutWindow.Content?.XamlRoot;
        PopOutButton.Visibility = isInPopOut
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void PopOutButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (App.PopOutWindow is not null)
        {
            App.PopOutWindow.Activate();
            return;
        }

        App.PopOutWindow = new PopOutWindow();
        App.PopOutWindow.Closed += (s, args) => App.PopOutWindow = null;
        App.PopOutWindow.Activate();
    }

    private void MailListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ConversationGroup group)
        {
            if (group.HasMultipleMessages)
                group.IsExpanded = !group.IsExpanded;
            else
                ViewModel.NavigateToMailDetail(group.LatestMessage);
        }
    }

    private async void ArchiveSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (args.SwipeControl.DataContext is not ConversationGroup group)
            return;

        await ViewModel.ArchiveThisMailItem(group.LatestMessage);
    }

    private void ReadSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (args.SwipeControl.DataContext is not ConversationGroup group)
            return;

        foreach (MailData message in group.Messages)
            ViewModel.MarkMessageIsReadAs(message, true);
    }

    private void FocusedOtherSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.IsFocusedView = sender.SelectedItem == FocusedSelectorBarItem;
    }
}
