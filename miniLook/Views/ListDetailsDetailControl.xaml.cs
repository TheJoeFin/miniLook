using CommunityToolkit.WinUI.Helpers;
using Microsoft.Graph.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using miniLook.Contracts.Services;
using miniLook.Models;
using miniLook.ViewModels;
using miniLook.Core.Helpers;
using miniLook.Helpers;
using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace miniLook.Views;

public sealed partial class ListDetailsDetailControl : UserControl
{
    private ListDetailsViewModel ViewModel { get; } = App.GetService<ListDetailsViewModel>();

    private string? _currentHtmlBody;
    private CancellationTokenSource _viewCancellation = new();
    private DispatcherQueueTimer? _browserIdleTimer;
    private bool _released;
    private bool _rendering;

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
        Loaded += async (_, _) =>
        {
            _released = false;
            if (_viewCancellation.IsCancellationRequested)
            {
                _viewCancellation.Dispose();
                _viewCancellation = new();
            }
            await TryAutoRenderHtmlAsync();
        };
        Unloaded += (_, _) => ReleaseResources();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_released || BodyWebView?.CoreWebView2 is null)
            return;

        bool isDark = this.ActualTheme == ElementTheme.Dark;
        BodyWebView.DefaultBackgroundColor = isDark ? Colors.Black : Colors.White;
        BodyWebView.CoreWebView2.Profile.PreferredColorScheme = isDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;

        if (WebViewPane?.Visibility == Visibility.Visible
            && ListDetailsMenuItem is not null
            && !string.IsNullOrEmpty(_currentHtmlBody))
        {
            BodyWebView.NavigateToString(ApplyThemeToHtml(_currentHtmlBody));
        }
    }

    private static void OnListDetailsMenuItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListDetailsDetailControl control)
        {
            control._currentHtmlBody = null;
            control._viewCancellation.Cancel();
            control._viewCancellation.Dispose();
            control._viewCancellation = new();
            control.ResetView();
            if (!control._released)
            {
                _ = control.TryAutoRenderHtmlAsync();
                if (control.ListDetailsMenuItem is MailData mail)
                    _ = control.ViewModel.LoadPendingAttachmentsAsync(mail, control._viewCancellation.Token);
            }
        }
    }

    private void ResetView()
    {
        MessageDetailsButton.Flyout.Hide();
        BodyViewButton.Label = "HTML view";
        ReleaseBrowser();
        BodyScrollViewer.Visibility = Visibility.Visible;
    }

    private async Task TryAutoRenderHtmlAsync()
    {
        if (_released || ListDetailsMenuItem is null || !ListDetailsMenuItem.HasHtmlBody)
            return;
        try
        {
            bool enabled = await App.GetService<ILocalSettingsService>()
                .ReadSettingAsync<bool>(SettingsViewModel.AlwaysRenderHtmlSettingsKey);
            if (enabled && !_released)
                await ShowHtmlAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTML] Could not read the automatic HTML preference: {ex.Message}");
            ShowError("Html_Error");
        }
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null)
            return;

        ViewModel.PopOutMailItem(ListDetailsMenuItem);
    }

    private async void BodyViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebViewPane?.Visibility == Visibility.Visible)
        {
            WebViewPane.Visibility = Visibility.Collapsed;
            BodyScrollViewer.Visibility = Visibility.Visible;
            BodyViewButton.Label = "HTML view";
            if (_browserIdleTimer is null)
            {
                _browserIdleTimer = DispatcherQueue.CreateTimer();
                _browserIdleTimer.Interval = TimeSpan.FromMinutes(1);
                _browserIdleTimer.IsRepeating = false;
                _browserIdleTimer.Tick += OnBrowserIdle;
            }
            _browserIdleTimer.Start();
            return;
        }
        await ShowHtmlAsync();
    }

    private async Task ShowHtmlAsync()
    {
        if (_released || _rendering || ListDetailsMenuItem is not MailData mail || !mail.HasHtmlBody)
            return;
        CancellationToken cancellationToken = _viewCancellation.Token;
        _rendering = true;
        BodyViewButton.IsEnabled = false;
        _browserIdleTimer?.Stop();
        try
        {
            string html = _currentHtmlBody ?? await ViewModel.GetHtmlBodyAsync(mail.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(html))
                throw new InvalidDataException("The message HTML could not be loaded.");
            _currentHtmlBody = html;

            FindName(nameof(WebViewPane));
            BodyScrollViewer.Visibility = Visibility.Collapsed;
            WebViewPane.Visibility = Visibility.Visible;
            BodyViewButton.Label = "Text view";
            await BodyWebView.EnsureCoreWebView2Async();
            cancellationToken.ThrowIfCancellationRequested();
            BodyWebView.NavigateToString(ApplyThemeToHtml(_currentHtmlBody));
            ViewModel.MarkMessageIsReadAs(mail, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("[HTML] Rendering canceled because the detail view changed.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HTML] Could not render the message: {ex.Message}");
            if (!_released)
            {
                ResetView();
                ShowError("Html_Error");
            }
        }
        finally
        {
            _rendering = false;
            BodyViewButton.IsEnabled = true;
            if (!_released && !ReferenceEquals(mail, ListDetailsMenuItem))
                _ = TryAutoRenderHtmlAsync();
        }
    }

    private void BodyWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is not null || sender.CoreWebView2 is null || _released)
            return;
        bool isDark = this.ActualTheme == ElementTheme.Dark;
        sender.DefaultBackgroundColor = isDark ? Colors.Black : Colors.White;
        sender.CoreWebView2.Profile.PreferredColorScheme = isDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
        sender.CoreWebView2.NewWindowRequested -= OnBrowserNewWindow;
        sender.CoreWebView2.NewWindowRequested += OnBrowserNewWindow;
    }

    private void OnBrowserNewWindow(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
    }

    private void OnBrowserIdle(DispatcherQueueTimer sender, object args)
    {
        if (WebViewPane?.Visibility != Visibility.Visible)
        {
            ReleaseBrowser();
            _currentHtmlBody = null;
        }
    }

    private void ReleaseBrowser()
    {
        _browserIdleTimer?.Stop();
        if (BodyWebView?.CoreWebView2 is not null)
            BodyWebView.CoreWebView2.NewWindowRequested -= OnBrowserNewWindow;
        BodyWebView?.Close();
        if (WebViewPane is not null)
            UnloadObject(WebViewPane);
    }

    public void ReleaseResources()
    {
        _released = true;
        _viewCancellation.Cancel();
        ReleaseBrowser();
        if (_browserIdleTimer is not null)
        {
            _browserIdleTimer.Tick -= OnBrowserIdle;
            _browserIdleTimer = null;
        }
        _currentHtmlBody = null;
    }

    private void ShowError(string resource)
    {
        if (_released)
            return;
        StatusInfoBar.Message = resource.GetLocalized();
        StatusInfoBar.IsOpen = true;
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

        string messageId = ListDetailsMenuItem.Id;
        CancellationToken cancellationToken = _viewCancellation.Token;
        button.IsEnabled = false;
        try
        {
            string folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Attachments");
            string tempPath = attachment.LocalFilePath
                ?? Path.Combine(folder, AttachmentFileName.Create(messageId, attachment.Id, attachment.Name));
            if (!File.Exists(tempPath))
            {
                byte[]? content = await ViewModel.GetAttachmentContentAsync(messageId, attachment.Id, cancellationToken);
                if (content is null || content.Length == 0)
                    throw new InvalidDataException("The attachment content could not be downloaded.");
                Directory.CreateDirectory(folder);
                await AtomicFile.WriteAsync(tempPath, (stream, token) => stream.WriteAsync(content, token).AsTask(), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            attachment.LocalFilePath = tempPath;
            Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await Windows.System.Launcher.LaunchFileAsync(file))
                throw new IOException("Windows could not open the attachment.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine("[Attachments] Download canceled because the detail view changed.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open attachment: {ex.Message}");
            ShowError("Attachment_Error");
        }
        finally
        {
            button.IsEnabled = true;
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