using System;
using System.Runtime.InteropServices;

namespace SonosDesk.Windows.Interop;

internal static partial class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const uint ModNoRepeat = 0x4000;
    public const uint AbmGetTaskbarPos = 0x00000005;
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int DwmwcpRound = 2;
    public const int DwmwcpRoundSmall = 3;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("shell32.dll")]
    public static partial UIntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint CbSize;
        public IntPtr HWnd;
        public uint UCallbackMessage;
        public uint UEdge;
        public Rect Rc;
        public IntPtr LParam;
    }
}
