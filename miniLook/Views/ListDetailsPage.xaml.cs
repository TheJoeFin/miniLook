using Microsoft.UI.Xaml.Controls;
using miniLook.Models;
using miniLook.ViewModels;
using Windows.Foundation;

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

        UpdateInfoBadgePositions();
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

    private void FocusedOtherSelectorBar_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        UpdateInfoBadgePositions();
    }

    private void UpdateInfoBadgePositions()
    {
        try
        {
            var focusedTransform = FocusedSelectorBarItem.TransformToVisual(SelectorBarContainer);
            var focusedBounds = focusedTransform.TransformBounds(
                new Rect(0, 0, FocusedSelectorBarItem.ActualWidth, FocusedSelectorBarItem.ActualHeight));
            FocusedInfoBadge.Margin = new Microsoft.UI.Xaml.Thickness(focusedBounds.Right - 10, 2, 0, 0);

            var otherTransform = OtherSelectorBarItem.TransformToVisual(SelectorBarContainer);
            var otherBounds = otherTransform.TransformBounds(
                new Rect(0, 0, OtherSelectorBarItem.ActualWidth, OtherSelectorBarItem.ActualHeight));
            OtherInfoBadge.Margin = new Microsoft.UI.Xaml.Thickness(otherBounds.Right - 10, 2, 0, 0);
        }
        catch { }
    }
}
