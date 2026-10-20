// Tray.cs - a second, tiny Onsay in the notification area who watches the cursor, redrawn only when his face changes.
//
// 1. Tray(click)    adds the icon; click runs on a left click
// 2. WndProc        clicks, and adding the icon again if Explorer restarts
// 3. Tick()         points him at the cursor, renders him at icon size and swaps the icon if he changed
// 4. Snapshot()     his face as straight BGRA pixels, or null when unchanged
// 5. IconOf(pixels) a Windows icon from those pixels
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Onsay.Character;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

sealed class Tray : IDisposable
{
    ////////// WIN32 //////////

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }

    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bits, byte[]? data);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const int Callback = 0x8001;   // WM_APP + 1, how the icon tells us about clicks
    const double Body = 64;        // DIPs, the size he is drawn at before shrinking to the icon
    static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    ////////// STATE //////////

    readonly OnsayView him = new() { Width = Body, Height = Body };
    readonly Action click;
    readonly HwndSource window;
    readonly DispatcherTimer timer;
    IntPtr icon;
    byte[] lastFrame = Array.Empty<byte>();
    RECT spot;
    int spotAge;

    // 1. A hidden window of our own receives the icon's clicks. Unlike a message only window, it hears TaskbarCreated.
    public Tray(Action click)
    {
        this.click = click;
        window = new HwndSource(new HwndSourceParameters("Onsay tray") { WindowStyle = 0, Width = 0, Height = 0 });
        window.AddHook(WndProc);

        him.Measure(new Size(Body, Body));
        him.Arrange(new Rect(0, 0, Body, Body));
        him.Mood("calm");

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        Tick();
        Notify(NIM_ADD);
    }

    bool Notify(uint msg)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = window.Handle, uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = Callback, hIcon = icon, szTip = "Onsay",
            szInfo = "", szInfoTitle = "",
        };
        return Shell_NotifyIcon(msg, ref data);
    }

    // 2.
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Callback && (int)lParam == (int)WM_LBUTTONUP) { him.React("greet"); click(); }
        if (msg == TaskbarCreated) { spotAge = 0; Notify(NIM_ADD); }
        return IntPtr.Zero;
    }

    // 3. Where the icon sits is asked about once a second, since the tray rarely moves and asking crosses into Explorer.
    //    Null when it is hidden in the overflow, so he looks out at you.
    void Tick()
    {
        if (spotAge-- <= 0)
        {
            var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = window.Handle, uID = 1 };
            if (Shell_NotifyIconGetRect(ref id, out spot) != 0) spot = default;
            spotAge = 20;
        }
        double scale = GetDpiForSystem() / 96.0;
        if (spot.Width > 0 && GetCursorPos(out var c))
            him.Watch(new Point((c.X - (spot.Left + spot.Width / 2.0)) / scale, (c.Y - (spot.Top + spot.Height / 2.0)) / scale));
        else him.Watch(null);

        int size = GetSystemMetricsForDpi(49, GetDpiForSystem());   // 49 SM_CXSMICON, the tray's icon size
        if (Snapshot(size) is not { } pixels) return;
        var old = icon;
        icon = IconOf(pixels, size);
        Notify(NIM_MODIFY);
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    // 4. Icons want straight alpha, not WPF's premultiplied.
    byte[]? Snapshot(int size)
    {
        him.UpdateLayout();
        var face = new DrawingVisual();
        using (var dc = face.RenderOpen())
            dc.DrawRectangle(new VisualBrush(him) { Stretch = Stretch.Uniform }, null, new Rect(0, 0, size, size));
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(face);

        var pixels = new byte[size * size * 4];
        new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, size * 4, 0);
        if (pixels.AsSpan().SequenceEqual(lastFrame)) return null;
        return lastFrame = pixels;
    }

    // 5. The mask is ignored when the colour has alpha, but Windows still wants one.
    static IntPtr IconOf(byte[] pixels, int size)
    {
        IntPtr color = CreateBitmap(size, size, 1, 32, pixels), mask = CreateBitmap(size, size, 1, 1, null);
        var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var made = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        return made;
    }

    public void Dispose()
    {
        timer.Stop();
        Notify(NIM_DELETE);
        window.Dispose();
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }
}
