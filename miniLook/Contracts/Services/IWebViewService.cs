using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace miniLook.Contracts.Services;

public interface IWebViewService
{
    Uri? Source
    {
        get;
    }

    bool CanGoBack
    {
        get;
    }

    bool CanGoForward
    {
        get;
    }

    bool StayInOneWindow { get; set; }

    event EventHandler<CoreWebView2WebErrorStatus>? NavigationCompleted;

    void Initialize(WebView2 webView);

    void GoBack();

    void GoForward();

    void Reload();

    Task GoToString(string htmlToRender);

    void UnregisterEvents();
}
