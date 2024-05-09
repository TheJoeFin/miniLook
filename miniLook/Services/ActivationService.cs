﻿using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using miniLook.Activation;
using miniLook.Contracts.Services;
using miniLook.ViewModels;
using miniLook.Views;

namespace miniLook.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IGraphService _graphService;
    private readonly INavigationService _navigationService;
    private UIElement? _shell = null;

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService, IGraphService graphService, INavigationService navigationService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _graphService = graphService;
        _navigationService = navigationService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        // Execute tasks before activation.
        await InitializeAsync();

        // Set the MainWindow Content.
        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        // Handle activation via ActivationHandlers.
        await HandleActivationAsync(activationArgs);

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // Execute tasks after activation.
        await StartupAsync();
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        IActivationHandler? activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
            await activationHandler.HandleAsync(activationArgs);

        if (_defaultHandler.CanHandle(activationArgs))
            await _defaultHandler.HandleAsync(activationArgs);
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await Task.CompletedTask;
        await _graphService.InitializeAsync();
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();

        if (_graphService.IsAuthenticated)
            _navigationService.NavigateTo(typeof(ListDetailsViewModel).FullName!);

        await Task.CompletedTask;
    }
}
