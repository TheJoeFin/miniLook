using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using miniLook.Helpers;
using miniLook.Models;

namespace miniLook.Views;

public sealed partial class ListDetailsDetailControl : UserControl
{
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
        ListDetailsPage? parentListPage = this.FindParentOfType<ListDetailsPage>();

        if (ListDetailsMenuItem is null
            || parentListPage is null
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        parentListPage.ViewModel.RenderMailBody(ListDetailsMenuItem);
        MarkMessageIsReadAs(true);
    }

    private void TryUpdateParent()
    {
        ListDetailsPage? parentListPage = this.FindParentOfType<ListDetailsPage>();
        if (parentListPage is null || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        parentListPage.ViewModel.UpdateItems();
    }

    private async void ArchiveHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        ListDetailsPage? parentListPage = this.FindParentOfType<ListDetailsPage>();

        if (ListDetailsMenuItem is null
            || ProviderManager.Instance.GlobalProvider is not MsalProvider provider
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable
            || parentListPage is null)
            return;

        await parentListPage.ViewModel.ArchiveThisMailItem(ListDetailsMenuItem);
    }

    private void MarkMessageIsReadAs(bool isRead)
    {
        ListDetailsPage? parentListPage = this.FindParentOfType<ListDetailsPage>();

        if (ListDetailsMenuItem is null
            || ProviderManager.Instance.GlobalProvider is not MsalProvider provider
            || parentListPage is null)
            return;

        GraphServiceClient _graphClient = provider.GetClient();
        _ = _graphClient.Me
            .MailFolders
            .Inbox
            .Messages[ListDetailsMenuItem.Id]
            .Request()
            .UpdateAsync(new Message { IsRead = isRead });
    }

    private void ReplyHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        ListDetailsPage? parentListPage = this.FindParentOfType<ListDetailsPage>();
        if (parentListPage is null
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        MarkMessageIsReadAs(true);
        parentListPage.ViewModel.ReplyToThisMailItem(ListDetailsMenuItem);
    }

    private void ForwardHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        ListDetailsPage? parentListPage = this.FindParentOfType<ListDetailsPage>();
        if (parentListPage is null
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        MarkMessageIsReadAs(true);
        parentListPage.ViewModel.ForwardThisMailItem(ListDetailsMenuItem);
    }

    private async void DeleteHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null
            || ProviderManager.Instance.GlobalProvider is not MsalProvider provider
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        GraphServiceClient _graphClient = provider.GetClient();

        MailFolder? deletedFolder = _graphClient.Me
            .MailFolders
            .Request()
            .Filter("displayName eq 'Deleted Items'")
            .GetAsync()
            .Result
            .FirstOrDefault();

        if (deletedFolder is null)
            return;

        try
        {
            // instead of using DeleteAsync() method, we move the message to the Deleted Items folder
            // this is what Outlook does when you delete a message
            // not sure about the translation issues with matching the folder name

            _ = await _graphClient.Me
                .MailFolders
                .Inbox
                .Messages[ListDetailsMenuItem.Id]
                .Move(deletedFolder.Id)
                .Request()
                .PostAsync();
        }
        catch (Exception)
        {
#if DEBUG
            throw;
#endif
        }

        TryUpdateParent();
    }

    private void MarkReadHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        MarkMessageIsReadAs(true);
        TryUpdateParent();
    }

    private void MarkUnreadHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        MarkMessageIsReadAs(false);
        TryUpdateParent();
    }
}
