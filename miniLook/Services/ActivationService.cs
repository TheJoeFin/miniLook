using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using miniLook.Activation;
using miniLook.Contracts.Services;
using miniLook.ViewModels;
using miniLook.Views;

namespace miniLook.Services;

public class ActivationService : IActivationService
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IGraphService _graphService;

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService, IGraphService graphService, INavigationService navigationService)
    {
        _themeSelectorService = themeSelectorService;
        _graphService = graphService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        // Execute tasks before activation.
        await InitializeAsync();

        // Set minimal MainWindow Content (required for WinUI lifecycle).
        // The actual UI is displayed in the tray flyout, not in MainWindow.
        if (App.MainWindow.Content == null)
        {
            App.MainWindow.Content = new Frame();
        }

        // Activate the MainWindow (required for WinUI initialization, then hidden by App).
        App.MainWindow.Activate();

        // Execute tasks after activation.
        await StartupAsync();
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await _graphService.InitializeAsync();
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
    }
}
