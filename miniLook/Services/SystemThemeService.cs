using System.Diagnostics;
using System.Security;

using Microsoft.Win32;

namespace miniLook.Services;

internal static class SystemThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLight => ReadFlag("SystemUsesLightTheme");

    public static bool ShowsAccentColor => !IsLight && ReadFlag("ColorPrevalence");

    private static bool ReadFlag(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue(name) is int value && value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"Cannot read the taskbar theme setting '{name}': {ex}");
            return false;
        }
    }
}