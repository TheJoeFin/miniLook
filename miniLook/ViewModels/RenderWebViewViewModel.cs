using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Graph.Models;
using Microsoft.Web.WebView2.Core;

using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;

namespace miniLook.ViewModels;

// TODO: Review best practices and distribution guidelines for WebView2.
// https://docs.microsoft.com/microsoft-edge/webview2/get-started/winui
// https://docs.microsoft.com/microsoft-edge/webview2/concepts/developer-guide
// https://docs.microsoft.com/microsoft-edge/webview2/concepts/distribution
public partial class RenderWebViewViewModel : ObservableRecipient, INavigationAware
{
    private CancellationTokenSource? _renderCancellation;
    private MailData? passedMailData;

    // TODO: Set the default URL to display.
    [ObservableProperty]
    private Uri source = new("https://docs.microsoft.com/windows/apps/");

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasFailures;

    public IWebViewService WebViewService { get; }
    public INavigationService NavigationService { get; }
    private IGraphService GraphService { get; }

    public RenderWebViewViewModel(IWebViewService webViewService, INavigationService navigationService, IGraphService graphService)
    {
        WebViewService = webViewService;
        WebViewService.StayInOneWindow = true;
        NavigationService = navigationService;
        GraphService = graphService;
    }

    [RelayCommand]
    private async Task OpenInBrowser()
    {
        if (WebViewService.Source is not null)
            await Windows.System.Launcher.LaunchUriAsync(WebViewService.Source);
    }

    [RelayCommand]
    private void Reload()
    {
        WebViewService.Reload();
    }

    [RelayCommand(CanExecute = nameof(BrowserCanGoForward))]
    private void BrowserForward()
    {
        if (WebViewService.CanGoForward)
            WebViewService.GoForward();
    }

    private bool BrowserCanGoForward()
    {
        return WebViewService.CanGoForward;
    }

    [RelayCommand(CanExecute = nameof(BrowserCanGoBack))]
    private void BrowserBack()
    {
        if (WebViewService.CanGoBack)
            WebViewService.GoBack();
    }

    [RelayCommand]
    private void Close()
    {
        NavigationService.NavigateTo(typeof(ListDetailsViewModel).FullName!, passedMailData);
    }

    private bool BrowserCanGoBack()
    {
        return WebViewService.CanGoBack;
    }

    public async void OnNavigatedTo(object parameter)
    {
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = new();
        CancellationToken cancellationToken = _renderCancellation.Token;
        WebViewService.NavigationCompleted -= OnNavigationCompleted;
        WebViewService.NavigationCompleted += OnNavigationCompleted;
        if (parameter is MailData mailData)
        {
            passedMailData = mailData;

            if (GraphService.Client is not null)
            {
                try
                {
                    Message? message = await GraphService.Client.Me.Messages[mailData.Id].GetAsync(config =>
                    {
                        config.QueryParameters.Select = ["body"];
                    }, cancellationToken: cancellationToken);

                    if (message?.Body?.ContentType == BodyType.Html && !string.IsNullOrEmpty(message.Body.Content))
                        await WebViewService.GoToString(message.Body.Content, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine("[HTML] Rendering canceled after navigation.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HTML] Rendering failed: {ex.Message}");
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        HasFailures = true;
                        IsLoading = false;
                    }
                }
            }
        }

    }

    public void OnNavigatedFrom()
    {
        _renderCancellation?.Cancel();
        WebViewService.UnregisterEvents();
        WebViewService.NavigationCompleted -= OnNavigationCompleted;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2WebErrorStatus webErrorStatus)
    {
        IsLoading = false;
        BrowserBackCommand.NotifyCanExecuteChanged();
        BrowserForwardCommand.NotifyCanExecuteChanged();

        if (webErrorStatus != default)
        {
            HasFailures = true;
        }
    }

    [RelayCommand]
    private void OnRetry()
    {
        HasFailures = false;
        IsLoading = true;
        WebViewService?.Reload();
    }
}