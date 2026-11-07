namespace miniLook.Contracts.Services;

public interface IBackgroundSyncService
{
    bool BackgroundEnabled { get; }
    Task StartAsync();
    Task StopAsync();
    Task SetBackgroundEnabledAsync(bool enabled);
    void SetForegroundActive(bool active);
}