using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SonosDesk.Windows.Interop;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

public static class WindowsShell
{
    public static TaskbarEdge GetTaskbarEdge()
    {
        var data = new NativeMethods.AppBarData { CbSize = (uint)Marshal.SizeOf<NativeMethods.AppBarData>() };
        var result = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data);
        return result == UIntPtr.Zero || data.UEdge > 3 ? TaskbarEdge.Bottom : (TaskbarEdge)data.UEdge;
    }

    public static void ApplyRoundedCorners(IntPtr hwnd, bool small = false)
    {
        var preference = small ? NativeMethods.DwmwcpRoundSmall : NativeMethods.DwmwcpRound;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    public static void ApplyDarkTitleBar(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    public static bool BringToForeground(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    public static bool IsAppDarkMode() => ReadPersonalize("AppsUseLightTheme") == 0;

    public static bool IsSystemDarkMode() => ReadPersonalize("SystemUsesLightTheme") == 0;

    private static int? ReadPersonalize(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
