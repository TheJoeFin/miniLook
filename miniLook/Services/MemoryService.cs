namespace miniLook.Services;

internal static class MemoryService
{
    public static void ReleaseIdle(CancellationToken cancellationToken) => _ = Task.Run(() =>
    {
        if (!cancellationToken.IsCancellationRequested)
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
    });
}