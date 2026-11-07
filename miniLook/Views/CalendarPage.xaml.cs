using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

using miniLook.Models;
using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class CalendarPage : Page, ITrayFlyoutPage
{
    public CalendarViewModel ViewModel { get; }

    public CollectionViewSource GroupedEvents { get; } = new()
    {
        IsSourceGrouped = true,
        ItemsPath = new PropertyPath(nameof(CalendarEventDayGroup.Items)),
    };

    public CalendarPage(CalendarViewModel viewModel)
    {
        ViewModel = viewModel;
        GroupedEvents.Source = ViewModel.EventGroups;
        InitializeComponent();
    }

    public void OnShown() => ViewModel.OnShown();

    public void OnHidden() => ViewModel.OnHidden();

    public void OnClosed() => ViewModel.OnClosed();

    public void StartNewEvent() => ViewModel.StartNewEvent();

    private void NewEventButton_Click(object sender, RoutedEventArgs e) => StartNewEvent();

    private void CalendarList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CalendarEventItem item)
            ViewModel.EditEventCommand.Execute(item);
    }

    private async void DeleteEditingEventButton_Click(object sender, RoutedEventArgs e)
    {
        CalendarEventItem? item = ViewModel.GetEditingEvent();
        if (item is not null)
            await DeleteEventAsync(item);
    }

    private async Task DeleteEventAsync(CalendarEventItem item)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "Delete event?",
            Content = $"Delete \"{item.Subject}\" from your calendar?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteEventAsync(item);
    }

    private async void OpenMeetingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string meetingLink }
            && Uri.TryCreate(meetingLink, UriKind.Absolute, out Uri? uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }

}
