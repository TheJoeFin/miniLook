using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using miniLook.Models;
using miniLook.ViewModels;

namespace miniLook.Views;

public sealed partial class ListDetailsDetailControl : UserControl
{
    private ListDetailsViewModel ViewModel { get; } = App.GetService<ListDetailsViewModel>();

    public MailData? ListDetailsMenuItem
    {
        get => GetValue(ListDetailsMenuItemProperty) as MailData;
        set => SetValue(ListDetailsMenuItemProperty, value);
    }

    public static readonly DependencyProperty ListDetailsMenuItemProperty = DependencyProperty.Register("ListDetailsMenuItem", typeof(MailData), typeof(ListDetailsDetailControl), new PropertyMetadata(null, OnListDetailsMenuItemPropertyChanged));

    public ListDetailsDetailControl()
    {
        InitializeComponent();
    }

    private static void OnListDetailsMenuItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListDetailsDetailControl control)
        {
            control.ForegroundElement.ChangeView(0, 0, 1);
        }
    }

    private void BrowserLink_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        ViewModel.RenderMailBody(ListDetailsMenuItem);
        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, true);
    }

    private void TryUpdateParent()
    {
        if (!NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        ViewModel.UpdateItems();
    }

    private async void ArchiveHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null
            || !ViewModel.HasGraphClient
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        await ViewModel.ArchiveThisMailItem(ListDetailsMenuItem);
    }

    private void ReplyHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, true);
        ViewModel.ReplyToThisMailItem(ListDetailsMenuItem);
    }

    private void ForwardHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, true);
        ViewModel.ForwardThisMailItem(ListDetailsMenuItem);
    }

    private async void DeleteHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null
            || !ViewModel.HasGraphClient
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        await ViewModel.DeleteThisMailItem(ListDetailsMenuItem);
        TryUpdateParent();
    }

    private void MarkReadHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, true);
        TryUpdateParent();
    }

    private void MarkUnreadHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, false);
        TryUpdateParent();
    }
}
