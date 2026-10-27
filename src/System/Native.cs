// Native.cs - the Win32 calls and window setup his windows need, in physical pixels.
//
// 1. MonitorAt(p, fallback)     the monitor under a point, with its work area
// 2. SomethingFullscreen()      a game, film or presentation has the whole screen
// 3. Place(hwnd, p, size)       moves a window, and sizes it when size is given, without activating it
// 4. Raise(hwnd)                above every other always on top window, without taking focus
// 5. PlaceTogether(moves)       several windows moved in one go, so they never draw apart
// 6. Hide/ShowSystemCursor()    Windows' own cursor hidden while his rides on the real mouse, and given back
// 7. Bare(window)               borderless, transparent, always on top, off screen and out of the taskbar
// 8. ClickThrough(window)       never clicked or activated, for a window that is only a picture. Returns its handle
// 9. Open(target)               a file, folder or link in its default app
// 10. InFront()               the app and title of the window in front that isn't one of his, like ("chrome", "Inbox")
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Onsay.Desktop;

static class Native
{
    ////////// TYPES //////////

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly POINT Center => new(Left + Width / 2, Top + Height / 2);
        public readonly RECT Inset(int by) => new() { Left = Left + by, Top = Top + by, Right = Right - by, Bottom = Bottom - by };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    // What the low level keyboard hook hands over for each key. Flag 0x10 is injected input.
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYHOOK { public uint vk, scan, flags, time; public IntPtr extra; }
    public delegate IntPtr HookProc(int code, IntPtr msg, IntPtr data);

    ////////// CONSTANTS //////////

    public const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, WM_SETTINGCHANGE = 0x1A, SPI_SETWORKAREA = 0x2F, WM_DISPLAYCHANGE = 0x7E, WM_HOTKEY = 0x312;
    public const uint MOD_ALT = 0x1, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    ////////// IMPORTS //////////

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool EndDeferWindowPos(IntPtr batch);
    [DllImport("user32.dll")] static extern IntPtr CreateCursor(IntPtr instance, int hotX, int hotY, int w, int h, byte[] and, byte[] xor);
    [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cursor, uint id);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int most);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    ////////// FUNCTIONS //////////

    // 1.
    public static MONITORINFO MonitorAt(POINT p, uint fallback)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(p, fallback), ref info);
        return info;
    }

    // 2. 2 busy, 3 fullscreen Direct3D, 4 presentation mode.
    public static bool SomethingFullscreen() => SHQueryUserNotificationState(out var s) == 0 && s is 2 or 3 or 4;

    // 3.
    public static void Place(IntPtr hwnd, POINT p, (int w, int h)? size = null) =>
        SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, size?.w ?? 0, size?.h ?? 0, (size is null ? SWP_NOSIZE : 0) | SWP_NOZORDER | SWP_NOACTIVATE);

    // 4.
    public static void Raise(IntPtr hwnd) => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // 5.
    public static void PlaceTogether(params (IntPtr hwnd, POINT p)[] moves)
    {
        var batch = BeginDeferWindowPos(moves.Length);
        foreach (var (hwnd, p) in moves) if (batch != IntPtr.Zero) batch = DeferWindowPos(batch, hwnd, IntPtr.Zero, p.X, p.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (batch != IntPtr.Zero) EndDeferWindowPos(batch);
    }

    // 6. Every shape of Windows' cursor swapped for an empty one, and back to the user's own scheme.
    static readonly uint[] Cursors = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 };
    public static void HideSystemCursor()
    {
        byte[] and = Enumerable.Repeat((byte)0xFF, 128).ToArray(), xor = new byte[128];   // 32 by 32, all clear
        foreach (var id in Cursors) SetSystemCursor(CreateCursor(IntPtr.Zero, 0, 0, 32, 32, and, xor), id);
    }
    public static void ShowSystemCursor() => SystemParametersInfo(0x57, 0, IntPtr.Zero, 0);   // SPI_SETCURSORS

    // 7.
    public static void Bare(Window w)
    {
        w.WindowStyle = WindowStyle.None;
        w.AllowsTransparency = true;
        w.Background = Brushes.Transparent;
        w.ResizeMode = ResizeMode.NoResize;
        w.ShowInTaskbar = false;
        w.Topmost = true;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = w.Top = -10000;
    }

    // 8.
    public static IntPtr ClickThrough(Window w)
    {
        w.ShowActivated = false;
        w.IsHitTestVisible = false;
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        return hwnd;
    }

    // 9.
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    /* 10. Down the windows from the one in front, past his own, like the chat, to the first shown with a title: the one the
           person was in. Its app is its program's name, as the core's window.list gives it. */
    public static (string app, string title) InFront()
    {
        var title = new System.Text.StringBuilder(512);
        for (var h = GetForegroundWindow(); h != IntPtr.Zero; h = GetWindow(h, 2))   // GW_HWNDNEXT
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pid == Environment.ProcessId || !IsWindowVisible(h) || IsIconic(h) || GetWindowText(h, title, title.Capacity) == 0) continue;
            try { return (Process.GetProcessById((int)pid).ProcessName, title.ToString()); } catch (ArgumentException) { return ("", title.ToString()); }
        }
        return ("", "");
    }
}
