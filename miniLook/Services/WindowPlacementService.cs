using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;

using WinUIEx;

namespace miniLook.Services;

internal static class WindowPlacementService
{
    private const uint ABM_GETTASKBARPOS = 0x5;
    private const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    private const int WindowMargin = 12;

    private static nint _trayIconWindowHandle;
    private static uint _trayIconId;

    // WinUIEx 2.9.3 keeps the notify-icon HWND private. Preserve that field for
    // trimming, as tray-folder does, so the shell can supply the real icon bounds.
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(TrayIcon))]
    public static void SetTrayIcon(TrayIcon trayIcon)
    {
        _trayIconId = trayIcon.TrayIconId;
        FieldInfo? field = typeof(TrayIcon).GetField("_windowHandle", BindingFlags.Instance | BindingFlags.NonPublic);
        _trayIconWindowHandle = field?.GetValue(trayIcon) switch
        {
            nint handle => handle,
            object boxed => ReadHandle(boxed),
            _ => 0,
        };
        if (_trayIconWindowHandle == 0)
            Debug.WriteLine("Tray icon window handle is unavailable; placement will fall back to the cursor.");
    }

    public static void PositionNearTray(Window window, int width, int height)
    {
        PointInt32 anchor;
        if (TryGetTrayIconRect(out RECT iconRect))
        {
            anchor = new((iconRect.left + iconRect.right) / 2, (iconRect.top + iconRect.bottom) / 2);
        }
        else if (PInvoke.GetCursorPos(out System.Drawing.Point cursor))
        {
            Debug.WriteLine("Tray icon bounds are unavailable; positioning near the cursor and taskbar.");
            anchor = new(cursor.X, cursor.Y);
        }
        else
        {
            Debug.WriteLine("Tray icon and cursor positions are unavailable; using the primary work area.");
            RectInt32 primary = DisplayArea.Primary.WorkArea;
            anchor = new(primary.X + primary.Width, primary.Y + primary.Height);
        }

        DisplayArea displayArea = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Primary);
        RectInt32 work = displayArea.WorkArea;
        uint dpi = GetDpiForPoint(anchor);
        int margin = ToPhysical(WindowMargin, dpi);
        int w = Math.Min(ToPhysical(width, dpi), Math.Max(1, work.Width - (2 * margin)));
        int h = Math.Min(ToPhysical(height, dpi), Math.Max(1, work.Height - (2 * margin)));

        int x = anchor.X - (w / 2);
        int y = work.Y + work.Height - h - margin;

        // SHAppBarMessage reports the primary taskbar, not necessarily the
        // taskbar on the icon's monitor. Never apply another monitor's bounds.
        if (TryGetTaskbarEdge(out RECT taskbar, out uint edge)
            && Intersects(taskbar, displayArea.OuterBounds))
        {
            switch (edge)
            {
                case ABE_BOTTOM:
                    y = Math.Min(taskbar.top, work.Y + work.Height) - h - margin;
                    break;
                case ABE_TOP:
                    y = Math.Max(taskbar.bottom, work.Y) + margin;
                    break;
                case ABE_LEFT:
                    x = Math.Max(taskbar.right, work.X) + margin;
                    y = anchor.Y - h + margin;
                    break;
                case ABE_RIGHT:
                    x = Math.Min(taskbar.left, work.X + work.Width) - w - margin;
                    y = anchor.Y - h + margin;
                    break;
            }
        }

        x = Math.Clamp(x, work.X + margin, Math.Max(work.X + margin, work.X + work.Width - w - margin));
        y = Math.Clamp(y, work.Y + margin, Math.Max(work.Y + margin, work.Y + work.Height - h - margin));
        window.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private static bool TryGetTrayIconRect(out RECT rect)
    {
        rect = default;
        if (_trayIconWindowHandle == 0)
            return false;

        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = (HWND)_trayIconWindowHandle,
            uID = _trayIconId,
        };
        return PInvoke.Shell_NotifyIconGetRect(in id, out rect).Succeeded
            && rect.right > rect.left && rect.bottom > rect.top;
    }

    private static bool TryGetTaskbarEdge(out RECT rect, out uint edge)
    {
        APPBARDATA data = new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (PInvoke.SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == 0)
        {
            rect = default;
            edge = ABE_BOTTOM;
            return false;
        }

        rect = data.rc;
        edge = data.uEdge;
        return true;
    }

    private static bool Intersects(RECT rect, RectInt32 bounds) =>
        rect.right > bounds.X && rect.left < bounds.X + bounds.Width
        && rect.bottom > bounds.Y && rect.top < bounds.Y + bounds.Height;

    private static uint GetDpiForPoint(PointInt32 point)
    {
        HMONITOR monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded)
            return dpiX;

        Debug.WriteLine("Monitor DPI is unavailable; using 96 DPI for tray placement.");
        return 96;
    }

    private static int ToPhysical(int logical, uint dpi) => (int)Math.Ceiling(logical * dpi / 96.0);

    private static nint ReadHandle(object boxed)
    {
        if (!boxed.GetType().IsValueType || Marshal.SizeOf(boxed) != IntPtr.Size)
            return 0;

        GCHandle pin = GCHandle.Alloc(boxed, GCHandleType.Pinned);
        try
        {
            return Marshal.ReadIntPtr(pin.AddrOfPinnedObject());
        }
        finally
        {
            pin.Free();
        }
    }
}