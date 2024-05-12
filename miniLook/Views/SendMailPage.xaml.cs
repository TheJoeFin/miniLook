using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class SendMailPage : Page
{
    private DispatcherTimer dispatcherTimer = new();

    public SendMailViewModel ViewModel
    {
        get;
    }

    public SendMailPage()
    {
        ViewModel = App.GetService<SendMailViewModel>();
        InitializeComponent();

        dispatcherTimer.Tick += DispatcherTimer_Tick;
        dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
    }

    private void DispatcherTimer_Tick(object? sender, object e)
    {
        dispatcherTimer.Stop();
        RecipientTokenizingTextBox.ClearValue(TokenizingTextBox.TextProperty);
    }

    private void RecipientTokenizingTextBox_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string clickedString)
            return;

        ViewModel.TryAddThisClickedItem(clickedString);
    }

    private void RecipientTokenizingTextBox_TokenItemAdding(TokenizingTextBox sender, TokenItemAddingEventArgs args)
    {
        ViewModel.TryAddThisClickedItem(args.TokenText);
    }

    private void RecipientTokenizingTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TokenizingTextBox senderTextBox)
            return;

        ViewModel.TryAddThisClickedItem(senderTextBox.Text);
        dispatcherTimer.Start();
    }
}
