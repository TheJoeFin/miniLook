using System.Diagnostics;

using Microsoft.UI.Dispatching;

using miniLook.Contracts.Services;
using miniLook.ViewModels;

namespace miniLook.Services;

public class BackgroundSyncService : IBackgroundSyncService
{
    private readonly ListDetailsViewModel _viewModel;
    private readonly IGraphService _graphService;
    private DispatcherQueue? _dispatcherQueue;
    private DispatcherQueueTimer? _timer;
    private bool _started;

    public BackgroundSyncService(ListDetailsViewModel viewModel, IGraphService graphService)
    {
        _viewModel = viewModel;
        _graphService = graphService;
    }

    public void Start()
    {
        if (_started)
            return;

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Background mail sync must start on the app dispatcher.");
        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick += OnTimerTick;

        _graphService.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _started = true;
        UpdateAuthenticationState();
    }

    public void Stop()
    {
        _started = false;
        _graphService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
        _dispatcherQueue = null;
    }

    private void OnAuthenticationStateChanged(object? sender, bool isSignedIn)
    {
        DispatcherQueue? dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null)
            return;

        if (dispatcherQueue.HasThreadAccess)
            UpdateAuthenticationState();
        else if (!dispatcherQueue.TryEnqueue(UpdateAuthenticationState))
            Debug.WriteLine("[Sync] Could not dispatch the authentication state change.");
    }

    private void UpdateAuthenticationState()
    {
        if (!_started || _timer is null)
            return;

        if (_graphService.IsAuthenticated)
        {
            _timer.Start();
            OnTimerTick(_timer, EventArgs.Empty);
        }
        else
            _timer.Stop();
    }

    private async void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_started && _graphService.IsAuthenticated)
            await _viewModel.RunBackgroundSyncAsync();
    }
}