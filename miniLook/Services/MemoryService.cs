using System.Diagnostics;
using System.Runtime;

using Windows.Win32;

namespace miniLook.Services;

internal static class MemoryService
{
    public static void ReleaseIdle() => _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        if (!PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue))
            Debug.WriteLine("Could not trim the idle working set after closing the tray flyout.");
    });
}