using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using miniLook.Helpers;
using miniLook.Models;
using Windows.System;

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
        if (ListDetailsMenuItem is null || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        // Launch the URI
        _ = Launcher.LaunchUriAsync(new Uri(ListDetailsMenuItem.WebLink));
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
        if (ListDetailsMenuItem is null 
            || ProviderManager.Instance.GlobalProvider is not MsalProvider provider
            || !NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            return;

        GraphServiceClient _graphClient = provider.GetClient();

        MailFolder? archiveFolder = _graphClient.Me
            .MailFolders
            .Request()
            .Filter("displayName eq 'Archive'")
            .GetAsync()
            .Result
            .FirstOrDefault();

        if (archiveFolder is null)
            return;

        try
        {
            _ = await _graphClient.Me
                .MailFolders
                .Inbox
                .Messages[ListDetailsMenuItem.Id]
                .Move(archiveFolder.Id)
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

    private void MarkMessageIsReadAs(bool isRead)
    {
        if (ListDetailsMenuItem is null
            || ProviderManager.Instance.GlobalProvider is not MsalProvider provider)
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
