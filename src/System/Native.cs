// Native.cs - the Win32 calls and window setup his windows need, in physical pixels.
//
// 1. MonitorAt(p, fallback)     the monitor under a point, with its work area, the strip his docked chat keeps given back
//    ScaleAt(p)                 the scaling of the monitor nearest a point, like 1.5 for 150%
// 2. SomethingFullscreen()      a game, film or presentation has the whole screen
// 3. Place(hwnd, p, size)       moves a window, and sizes it when size is given, without activating it
// 4. Raise(hwnd)                above every other always on top window, without taking focus
// 5. PlaceTogether(moves)       several windows moved in one go, so they never draw apart
// 6. Hide/ShowSystemCursor()    Windows' own cursor hidden while his rides on the real mouse, and given back
// 7. Bare(window)               borderless, transparent, always on top, off screen and out of the taskbar
// 8. ClickThrough(window)       never clicked or activated, for a window that is only a picture. Returns its handle
// 9. Open(target)               a file, folder or link in its default app
// 10. WebLink(target)           the address as a Uri when it is a web or mail link, or null
// 11. EndsWithThis(process)     a program that ends when this app does, however this app ends. What it starts goes on
// 12. Reserve(hwnd, edge, want)  keeps a strip of the screen for a window, the way the taskbar does, so other windows
//                               make room for it. Gives the strip Windows agreed to. Unreserve(hwnd) gives it back
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
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly POINT Center => new(Left + Width / 2, Top + Height / 2);
        public readonly RECT Inset(int by) => new() { Left = Left + by, Top = Top + by, Right = Right - by, Bottom = Bottom - by };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // What the low level keyboard hook hands over for each key. Flag 0x10 is injected input.
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYHOOK
    {
        public uint vk;
        public uint scan;
        public uint flags;
        public uint time;
        public IntPtr extra;
    }

    public delegate IntPtr HookProc(int code, IntPtr msg, IntPtr data);

    ////////// CONSTANTS //////////

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    const uint SWP_NOSIZE = 0x1;
    const uint SWP_NOMOVE = 0x2;
    const uint SWP_NOZORDER = 0x4;
    const uint SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const int WM_MOUSEACTIVATE = 0x21;
    public const int MA_NOACTIVATE = 3;
    public const int WM_SETTINGCHANGE = 0x1A;
    public const int SPI_SETWORKAREA = 0x2F;
    public const int WM_DISPLAYCHANGE = 0x7E;
    public const int WM_HOTKEY = 0x312;

    public const uint MOD_ALT = 0x1;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_NOREPEAT = 0x4000;

    ////////// IMPORTS //////////

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint dpiX, out uint dpiY);
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

    ////////// FUNCTIONS //////////

    /* 1. With the chat docked, Windows takes its strip out of the work area. Given back here, so he keeps his place on the
          taskbar and the chat measures its strip from the whole work area, as if it took no room. */
    public static MONITORINFO MonitorAt(POINT p, uint fallback)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(p, fallback), ref info);
        if (Reserved is not { } kept) return info;
        var mon = info.rcMonitor;
        bool here = kept.Left >= mon.Left && kept.Right <= mon.Right && kept.Top >= mon.Top && kept.Bottom <= mon.Bottom;
        if (!here) return info;
        if (info.rcWork.Left == kept.Right) info.rcWork.Left = kept.Left;
        else if (info.rcWork.Right == kept.Left) info.rcWork.Right = kept.Right;
        else if (info.rcWork.Bottom == kept.Top) info.rcWork.Bottom = kept.Bottom;
        return info;
    }

    // 1. Its effective DPI over 96, the way per monitor aware windows are sized there.
    public static double ScaleAt(POINT p)
    {
        const uint nearest = 2;
        bool known = GetDpiForMonitor(MonitorFromPoint(p, nearest), 0, out var dpi, out _) == 0;
        return known ? dpi / 96.0 : 1;
    }

    // 2. 2 busy, 3 fullscreen Direct3D, 4 presentation mode.
    public static bool SomethingFullscreen() => SHQueryUserNotificationState(out var s) == 0 && s is 2 or 3 or 4;

    // 3.
    public static void Place(IntPtr hwnd, POINT p, (int w, int h)? size = null)
    {
        uint keepSize = size is null ? SWP_NOSIZE : 0;
        SetWindowPos(hwnd, IntPtr.Zero, p.X, p.Y, size?.w ?? 0, size?.h ?? 0, keepSize | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // 4.
    public static void Raise(IntPtr hwnd) => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // 5.
    public static void PlaceTogether(params (IntPtr hwnd, POINT p)[] moves)
    {
        var batch = BeginDeferWindowPos(moves.Length);
        foreach (var (hwnd, p) in moves)
        {
            if (batch != IntPtr.Zero) batch = DeferWindowPos(batch, hwnd, IntPtr.Zero, p.X, p.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        if (batch != IntPtr.Zero) EndDeferWindowPos(batch);
    }

    // 6. Every shape of Windows' cursor swapped for an empty one, and back to the user's own scheme.
    static readonly uint[] Cursors = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 };
    public static void HideSystemCursor()
    {
        // 32 by 32, all clear.
        byte[] and = Enumerable.Repeat((byte)0xFF, 128).ToArray();
        byte[] xor = new byte[128];
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
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        return hwnd;
    }

    // 9.
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    // 10. Only these open from a reply: anything else stays words.
    public static Uri? WebLink(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme is "http" or "https" or "mailto" ? uri : null;
    }

    // 11. A job Windows closes when this app ends, even by a crash, ending what is in it. Silent breakaway keeps out what
    // that program starts, so a browser or app opened for the person stays open.
    static readonly IntPtr EndingJob = CreateJobObject(IntPtr.Zero, null);

    public static void EndsWithThis(Process process)
    {
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK;
        SetInformationJobObject(EndingJob, JobObjectExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf(limits));
        AssignProcessToJobObject(EndingJob, process.Handle);
    }

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    const uint JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr job, int kind, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint size);

    [DllImport("kernel32.dll")]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    ////////// 12. A STRIP OF THE SCREEN //////////

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint message, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);

    // Windows sends this to the window when something else moved the strips about, like the taskbar. Place it again then.
    public static readonly uint StripMoved = RegisterWindowMessage("Onsay.Strip");
    public static RECT? Reserved { get; private set; }   // the strip kept now, in pixels, or null

    /* 12. edge is Left, Right or Bottom. Windows may move the strip in, past the taskbar or another app's strip: the width
           or height asked for is kept from wherever it starts. */
    public static RECT Reserve(IntPtr hwnd, Edge edge, RECT want)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = StripMoved };
        if (Reserved is null) SHAppBarMessage(0, ref data);   // ABM_NEW
        data.uEdge = edge switch
        {
            Edge.Left => 0u,
            Edge.Right => 2u,
            _ => 3u,
        };
        int w = want.Width;
        int h = want.Height;
        data.rc = want;
        SHAppBarMessage(2, ref data);   // ABM_QUERYPOS: Windows moves rc clear of the strips there already
        if (edge == Edge.Left) data.rc.Right = data.rc.Left + w;
        if (edge == Edge.Right) data.rc.Left = data.rc.Right - w;
        if (edge == Edge.Bottom) data.rc.Top = data.rc.Bottom - h;
        SHAppBarMessage(3, ref data);   // ABM_SETPOS
        Reserved = data.rc;
        return data.rc;
    }

    // 12.
    public static void Unreserve(IntPtr hwnd)
    {
        if (Reserved is null) return;
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        SHAppBarMessage(1, ref data);   // ABM_REMOVE
        Reserved = null;
    }
}
