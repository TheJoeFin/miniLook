using CommunityToolkit.Authentication;

using Microsoft.UI.Dispatching;

using miniLook.Contracts.Services;
using miniLook.ViewModels;

namespace miniLook.Services;

public class BackgroundSyncService : IBackgroundSyncService
{
    private readonly ListDetailsViewModel _viewModel;
    private readonly IGraphService _graphService;
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

        _started = true;

        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
            return;

        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick += OnTimerTick;

        ProviderManager.Instance.ProviderStateChanged += OnProviderStateChanged;

        if (_graphService.IsAuthenticated)
            _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _started = false;
    }

    private void OnProviderStateChanged(object? sender, ProviderStateChangedEventArgs args)
    {
        if (args.NewState == ProviderState.SignedIn)
            _timer?.Start();
        else
            _timer?.Stop();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        _viewModel.RunBackgroundSync();
    }
}
