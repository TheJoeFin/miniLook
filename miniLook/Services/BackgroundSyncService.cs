using System.Diagnostics;
using Microsoft.UI.Dispatching;
using miniLook.Contracts.Services;
using miniLook.Core.Helpers;
using miniLook.ViewModels;
using Windows.Networking.Connectivity;

namespace miniLook.Services;

public class BackgroundSyncService : IBackgroundSyncService
{
    private const string BackgroundEnabledKey = "BackgroundTrayUpdates";

    // One connection change raises a burst of events. Each would otherwise cancel and restart the refresh.
    private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(2);

    private readonly ListDetailsViewModel _viewModel;
    private readonly IGraphService _graphService;
    private readonly ILocalSettingsService _settings;
    private DispatcherQueue? _dispatcherQueue;
    private DispatcherQueueTimer? _timer;
    private DispatcherQueueTimer? _networkSettleTimer;
    private bool _started;
    private bool _foregroundActive;
    private bool _refreshImmediately;
    private int _failures;
    private Task? _pollTask;

    public bool BackgroundEnabled { get; private set; } = true;

    public BackgroundSyncService(ListDetailsViewModel viewModel, IGraphService graphService, ILocalSettingsService settings)
    {
        _viewModel = viewModel;
        _graphService = graphService;
        _settings = settings;
    }

    public async Task StartAsync()
    {
        if (_started)
            return;

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Mail sync must start on the app dispatcher.");
        BackgroundEnabled = await _settings.ReadSettingAsync<bool?>(BackgroundEnabledKey) ?? true;
        _timer = _dispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;
        _networkSettleTimer = _dispatcherQueue.CreateTimer();
        _networkSettleTimer.IsRepeating = false;
        _networkSettleTimer.Interval = NetworkSettleDelay;
        _networkSettleTimer.Tick += OnNetworkSettled;
        _graphService.AuthenticationStateChanged += OnAuthenticationStateChanged;
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        _started = true;
        Restart();
    }

    public async Task StopAsync()
    {
        _started = false;
        _graphService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
        if (_networkSettleTimer is not null)
        {
            _networkSettleTimer.Stop();
            _networkSettleTimer.Tick -= OnNetworkSettled;
        }
        await _viewModel.CancelPendingSyncAsync();
        if (_pollTask is not null)
            await _pollTask;
        _timer = null;
        _networkSettleTimer = null;
        _dispatcherQueue = null;
    }

    public async Task SetBackgroundEnabledAsync(bool enabled)
    {
        await _settings.SaveSettingAsync(BackgroundEnabledKey, enabled);
        BackgroundEnabled = enabled;
        Restart();
    }

    public void SetForegroundActive(bool active)
    {
        if (_foregroundActive == active)
            return;
        _foregroundActive = active;

        switch (PollingPolicy.OnVisibilityChanged(active, CanPoll))
        {
            case VisibilityAction.RefreshNow:
                RefreshSoon();
                break;
            case VisibilityAction.ContinueQuietly:
                // A running pass reschedules itself at the hidden interval when it finishes.
                if (!IsPollRunning)
                {
                    _timer?.Stop();
                    ScheduleNextPoll();
                }
                break;
            default:
                _timer?.Stop();
                _refreshImmediately = false;
                _viewModel.CancelPendingSync();
                break;
        }
    }

    private bool CanPoll => _started && _graphService.IsAuthenticated && (_foregroundActive || BackgroundEnabled);

    private bool IsPollRunning => _pollTask is { IsCompleted: false };

    private void Restart()
    {
        _viewModel.CancelPendingSync();
        RefreshSoon();
    }

    private void RefreshSoon()
    {
        _timer?.Stop();
        _failures = 0;
        _refreshImmediately = CanPoll;
        if (CanPoll && !IsPollRunning)
            _pollTask = PollAsync();
    }

    private void OnAuthenticationStateChanged(object? sender, bool isSignedIn)
    {
        DispatcherQueue? dispatcher = _dispatcherQueue;
        if (dispatcher is null)
            return;

        // An expired token can surface on any thread and from any request, not only from a sync.
        void Apply()
        {
            _viewModel.OnSignInStateChanged(isSignedIn);
            Restart();
        }

        if (dispatcher.HasThreadAccess)
            Apply();
        else if (!dispatcher.TryEnqueue(Apply))
            Debug.WriteLine("[Sync] Could not dispatch a refresh after a sign-in change.");
    }

    private void OnNetworkStatusChanged(object sender)
    {
        DispatcherQueue? dispatcher = _dispatcherQueue;
        if (dispatcher is null)
            return;
        bool queued = dispatcher.TryEnqueue(() =>
        {
            _networkSettleTimer?.Stop();
            _networkSettleTimer?.Start();
        });
        if (!queued)
            Debug.WriteLine("[Sync] Could not dispatch a refresh after a network change.");
    }

    private void OnNetworkSettled(DispatcherQueueTimer sender, object args) => Restart();

    private void OnTimerTick(DispatcherQueueTimer sender, object args) => _pollTask = PollAsync();

    private async Task PollAsync()
    {
        _refreshImmediately = false;
        SyncOutcome outcome;
        try
        {
            outcome = await _viewModel.RunBackgroundSyncAsync(foreground: _foregroundActive);
        }
        catch (Exception ex)
        {
            // Nothing awaits this task while the app runs, so an escaped error would end polling for good.
            Debug.WriteLine($"[Sync] A refresh pass ended unexpectedly: {ex.Message}");
            outcome = SyncOutcome.Failed;
        }

        if (outcome == SyncOutcome.Failed)
            _failures++;
        else if (outcome == SyncOutcome.Succeeded)
            _failures = 0;

        ScheduleNextPoll();
    }

    private void ScheduleNextPoll()
    {
        if (!CanPoll || _timer is null)
            return;

        _timer.Interval = _refreshImmediately
            ? TimeSpan.FromMilliseconds(1)
            : PollingPolicy.GetInterval(_foregroundActive, _failures);
        _timer.Start();
    }
}
