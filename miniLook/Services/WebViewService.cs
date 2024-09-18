using System.Diagnostics.CodeAnalysis;

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

using miniLook.Contracts.Services;
using Windows.UI.WebUI;

namespace miniLook.Services;

public class WebViewService : IWebViewService
{
    private WebView2? _webView;

    public Uri? Source => _webView?.Source;

    [MemberNotNullWhen(true, nameof(_webView))]
    public bool CanGoBack => _webView != null && _webView.CanGoBack;

    [MemberNotNullWhen(true, nameof(_webView))]
    public bool CanGoForward => _webView != null && _webView.CanGoForward;

    public bool StayInOneWindow { get; set; } = false;

    public event EventHandler<CoreWebView2WebErrorStatus>? NavigationCompleted;

    public WebViewService()
    {
    }

    [MemberNotNull(nameof(_webView))]
    public void Initialize(WebView2 webView)
    {
        _webView = webView;
        _webView.NavigationCompleted += OnWebViewNavigationCompleted;
    }

    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (!StayInOneWindow)
        {
            args.Handled = false;
            return;
        }

        args.Handled = true;
        // No need to wait for the launcher to finish sending the URI to the browser
        // before we allow the WebView2 in our app to continue.
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri));
        // LaunchUriAsync is the WinRT API for launching a URI.
        // Another option not involving WinRT might be System.Diagnostics.Process.Start(args.Uri);
    }

    public void GoBack() => _webView?.GoBack();

    public void GoForward() => _webView?.GoForward();

    public void Reload() => _webView?.Reload();

    public async Task GoToString(string htmlToRender)
    {
        if (_webView is null)
            return;

        await _webView.EnsureCoreWebView2Async();
        _webView.NavigateToString(htmlToRender);
        _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
    }

    public void UnregisterEvents()
    {
        if (_webView != null)
        {
            _webView.NavigationCompleted -= OnWebViewNavigationCompleted;
        }
    }

    private void OnWebViewNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => NavigationCompleted?.Invoke(this, args.WebErrorStatus);
}
