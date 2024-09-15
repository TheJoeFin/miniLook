using Microsoft.UI.Xaml.Controls;

using miniLook.ViewModels;

namespace miniLook.Views;

// To learn more about WebView2, see https://docs.microsoft.com/microsoft-edge/webview2/.
public sealed partial class RenderWebViewPage : Page
{
    public RenderWebViewViewModel ViewModel
    {
        get;
    }

    public RenderWebViewPage()
    {
        ViewModel = App.GetService<RenderWebViewViewModel>();
        InitializeComponent();

        ViewModel.WebViewService.Initialize(WebView);
    }
}
