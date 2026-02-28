using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
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
            control.ResetToggles();
        }
    }

    private void ResetToggles()
    {
        SenderAddressText.Visibility = Visibility.Collapsed;
        SenderChevron.Glyph = "\uE76C";
        ToFullText.Visibility = Visibility.Collapsed;
        ToChevron.Glyph = "\uE76C";
        CcFullText.Visibility = Visibility.Collapsed;
        CcChevron.Glyph = "\uE76C";
        RelativeDateText.Visibility = Visibility.Visible;
        ExactDateText.Visibility = Visibility.Collapsed;
        WebViewPane.Visibility = Visibility.Collapsed;
        BodyScrollViewer.Visibility = Visibility.Visible;
    }

    private void SenderButton_Click(object sender, RoutedEventArgs e)
    {
        bool isExpanded = SenderAddressText.Visibility == Visibility.Visible;
        SenderAddressText.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        SenderChevron.Glyph = isExpanded ? "\uE76C" : "\uE70D";
    }

    private void ToButton_Click(object sender, RoutedEventArgs e)
    {
        bool isExpanded = ToFullText.Visibility == Visibility.Visible;
        ToFullText.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        ToChevron.Glyph = isExpanded ? "\uE76C" : "\uE70D";
    }

    private void CcButton_Click(object sender, RoutedEventArgs e)
    {
        bool isExpanded = CcFullText.Visibility == Visibility.Visible;
        CcFullText.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        CcChevron.Glyph = isExpanded ? "\uE76C" : "\uE70D";
    }

    private void DateButton_Click(object sender, RoutedEventArgs e)
    {
        bool showingExact = ExactDateText.Visibility == Visibility.Visible;
        RelativeDateText.Visibility = showingExact ? Visibility.Visible : Visibility.Collapsed;
        ExactDateText.Visibility = showingExact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null)
            return;

        ViewModel.PopOutMailItem(ListDetailsMenuItem);
    }

    private async void BrowserLink_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null || string.IsNullOrEmpty(ListDetailsMenuItem.HtmlBody))
            return;

        BodyScrollViewer.Visibility = Visibility.Collapsed;
        WebViewPane.Visibility = Visibility.Visible;
        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, true);

        await BodyWebView.EnsureCoreWebView2Async();
        BodyWebView.NavigateToString(ListDetailsMenuItem.HtmlBody);
    }

    private void BackToTextButton_Click(object sender, RoutedEventArgs e)
    {
        WebViewPane.Visibility = Visibility.Collapsed;
        BodyScrollViewer.Visibility = Visibility.Visible;
    }

    private void BodyWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        bool isDark = Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark;
        sender.DefaultBackgroundColor = isDark ? Colors.DimGray : Colors.LightGray;
        sender.CoreWebView2.Profile.PreferredColorScheme = isDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
        sender.CoreWebView2.NewWindowRequested += (s, ev) =>
        {
            ev.Handled = true;
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ev.Uri));
        };
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
