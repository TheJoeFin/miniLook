using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using miniLook.Contracts.Services;
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
        this.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (BodyWebView.CoreWebView2 is null)
            return;

        bool isDark = this.ActualTheme == ElementTheme.Dark;
        BodyWebView.DefaultBackgroundColor = isDark ? Colors.Black : Colors.White;
        BodyWebView.CoreWebView2.Profile.PreferredColorScheme = isDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;

        if (WebViewPane.Visibility == Visibility.Visible
            && ListDetailsMenuItem is not null
            && !string.IsNullOrEmpty(ListDetailsMenuItem.HtmlBody))
        {
            BodyWebView.NavigateToString(ApplyThemeToHtml(ListDetailsMenuItem.HtmlBody));
        }
    }

    private static async void OnListDetailsMenuItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListDetailsDetailControl control)
        {
            control.ForegroundElement.ChangeView(0, 0, 1);
            control.ResetToggles();
            await control.TryAutoRenderHtmlAsync();
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

    private async Task TryAutoRenderHtmlAsync()
    {
        if (ListDetailsMenuItem is null || string.IsNullOrEmpty(ListDetailsMenuItem.HtmlBody))
            return;

        ILocalSettingsService localSettingsService = App.GetService<ILocalSettingsService>();
        bool alwaysRenderHtml = await localSettingsService.ReadSettingAsync<bool>(ViewModels.SettingsViewModel.AlwaysRenderHtmlSettingsKey);

        if (!alwaysRenderHtml)
            return;

        BodyScrollViewer.Visibility = Visibility.Collapsed;
        WebViewPane.Visibility = Visibility.Visible;
        ViewModel.MarkMessageIsReadAs(ListDetailsMenuItem, true);

        await BodyWebView.EnsureCoreWebView2Async();
        BodyWebView.NavigateToString(ApplyThemeToHtml(ListDetailsMenuItem.HtmlBody));
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
        BodyWebView.NavigateToString(ApplyThemeToHtml(ListDetailsMenuItem.HtmlBody));
    }

    private void BackToTextButton_Click(object sender, RoutedEventArgs e)
    {
        WebViewPane.Visibility = Visibility.Collapsed;
        BodyScrollViewer.Visibility = Visibility.Visible;
    }

    private void BodyWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        bool isDark = this.ActualTheme == ElementTheme.Dark;
        sender.DefaultBackgroundColor = isDark ? Colors.Black : Colors.White;
        sender.CoreWebView2.Profile.PreferredColorScheme = isDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
        sender.CoreWebView2.NewWindowRequested += (s, ev) =>
        {
            ev.Handled = true;
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ev.Uri));
        };
    }

    private string ApplyThemeToHtml(string html)
    {
        if (this.ActualTheme != ElementTheme.Dark)
            return html;

        const string darkCss =
            "<style>" +
            ":root{color-scheme:dark;}" +
            "html,body{background-color:#1c1c1c!important;color:#d4d4d4!important;}" +
            "a:link,a:visited{color:#75b3f7!important;}" +
            "</style>";

        int headCloseIdx = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headCloseIdx >= 0)
            return html.Insert(headCloseIdx, darkCss);

        return darkCss + html;
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

    private async void AttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Models.AttachmentInfo attachment || ListDetailsMenuItem is null)
            return;

        byte[]? content = attachment.ContentBytes;

        if (content is null || content.Length == 0)
            content = await ViewModel.GetAttachmentContentAsync(ListDetailsMenuItem.Id, attachment.Id);

        if (content is null || content.Length == 0)
            return;

        try
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), attachment.Name);
            await System.IO.File.WriteAllBytesAsync(tempPath, content);
            attachment.ContentBytes = content;

            Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempPath);
            await Windows.System.Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open attachment: {ex.Message}");
        }
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
