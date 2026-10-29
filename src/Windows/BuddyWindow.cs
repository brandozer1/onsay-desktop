// BuddyWindow.cs - the window he lives in, hovering on the taskbar or wherever he was dropped, never taking focus.
//
// 1. SetOpen(on, focus)              the one place the chat opens or closes. He turns round so his back X closes it
// 2. Park(dropped, slide)            home: where he was dropped off the bar, or onto the bar nearest where he was dropped
// 3. MoveTo(p, slide)                jumps or slides him there
// 4. Put(p)                          puts him there now, and the chat with him in the same move
// 5. OnPress / OnDrag / OnRelease    a click opens or closes the chat, a drag carries him
// 6. Look()                          he follows the cursor, or his own pointer while it shows, and dozes when still
// 7. StepAside(on)                   hides while something is fullscreen, and comes back when it ends
// 8. BuildMenu()                     right click: open the chat, apps, settings, put him back, quit
// 9. Follow(), Fly                   while his own pointer shows, he flies beside it, then goes home, and the chat comes back
//                                    if it was open
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Onsay.Character;
using static Onsay.Desktop.Native;
using Spring = Onsay.Character.Spring;

namespace Onsay.Desktop;

sealed class BuddyWindow : Window
{
    ////////// SIZES //////////

    // In DIPs. Box is his window, the room around Body is for hops and leans. EndMargin keeps him off the very ends of
    // the bar, Gap is between his body and the bar.
    const double Box = 96, Body = 64, EndMargin = 12, Gap = 3;
    const int StopKey = 1;   // Alt+Shift+O's hotkey id
    static readonly TimeSpan Drowsy = TimeSpan.FromMinutes(3);

    ////////// STATE //////////

    readonly OnsayView him = new() { Width = Body, Height = Body };
    readonly Settings settings = Settings.Load();
    readonly ChatWindow chat;
    readonly Stopwatch clock = Stopwatch.StartNew();
    IntPtr hwnd;
    bool open, pressed, dragging, asleep, aside, sliding, chasing, reopen;
    POINT grab, lastCursor, seen;
    RECT grabRect;
    double lastMoveAt, slideLast;
    Vector velocity;
    (double x, double y) fly;   // where he is while following his pointer, kept unrounded so he moves evenly
    double flyLast;
    Spring? slideX, slideY;
    DateTime movedAt = DateTime.Now;

    public BuddyWindow()
    {
        Title = "Onsay";
        OnsayView.Paint(settings.Look ?? new());
        Bare(this);
        ShowActivated = false;
        Width = Height = Box;
        Content = him;
        chat = new ChatWindow(settings, Toggle);

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            // The hook swallows the key on its own thread. The hotkey is there in case it cannot hook.
            Desktop.StopKey.Start(chat.Stop);
            RegisterHotKey(hwnd, StopKey, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, 'O');
        };
        Loaded += (_, _) =>
        {
            ShowSystemCursor();   // in case the app last ended while his pointer had Windows' own hidden
            Park(dropped: false);
            Every(TimeSpan.FromMilliseconds(33), () => { Follow(); Look(); });
            Every(TimeSpan.FromSeconds(1.5), () => StepAside(SomethingFullscreen()));
            him.React("greet");
            // The first time, he is set up, and then the chat opens.
            if (settings.SetUp != true) Later(() => SetupWindow.Open(settings, chat, () => Toggle(true)));
            new AskWindow(() => Rect().Inset(Pad));   // a lingo asking first, on a card beside him
            Updates.Start();
        };
        DpiChanged += (_, _) => { if (!dragging) Later(() => Park(dropped: false)); };
        Closed += (_, _) => { Core.Stop(); Application.Current.Shutdown(); };

        MouseLeftButtonDown += OnPress;
        MouseMove += OnDrag;
        MouseLeftButtonUp += (_, _) => OnRelease();
        LostMouseCapture += (_, _) => OnRelease();
        ContextMenu = BuildMenu();
    }

    double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;
    int Px(double dips) => (int)Math.Round(dips * Scale);
    int Pad => Px((Box - Body) / 2);   // his window's room around his body, in pixels
    RECT Rect() { GetWindowRect(hwnd, out var r); return r; }
    void Later(Action a) => Dispatcher.BeginInvoke(a, DispatcherPriority.Background);

    void Every(TimeSpan gap, Action tick)
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = gap };
        t.Tick += (_, _) => tick();
        t.Start();
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE) { handled = true; return MA_NOACTIVATE; }
        if (msg == WM_HOTKEY && wParam == StopKey) chat.Stop();
        // The taskbar moved or resized, or a monitor came or went. Let Windows finish first.
        if (msg == WM_DISPLAYCHANGE || (msg == WM_SETTINGCHANGE && wParam == SPI_SETWORKAREA)) Later(() => Park(dropped: false));
        return IntPtr.Zero;
    }

    ////////// 1. THE CHAT //////////

    // 1. focus false opens it without taking the keyboard, as when he comes back from using the computer.
    void SetOpen(bool on, bool focus = true)
    {
        open = on;
        him.Turn(on);
        him.Express(on ? "neutral" : "happy");
        if (on) FollowChat(focus);
        else chat.Dismiss();
    }

    // He stays above the chat, so even where it overlaps him he can always be grabbed.
    void FollowChat(bool focus)
    {
        var r = Rect();
        chat.Open(r.Inset(Pad), MonitorAt(r.Center, MONITOR_DEFAULTTONEAREST), Scale, focus);
        Raise(hwnd);
    }

    ////////// 2-4. WHERE HE SITS //////////

    // 2. Dropped on or near the bar he docks there, anywhere else he stays, kept on that screen.
    void Park(bool dropped, bool slide = false)
    {
        if (hwnd == IntPtr.Zero) return;
        var r = Rect();
        var body = r.Inset(Pad);
        if (dropped)
        {
            var here = MonitorAt(r.Center, MONITOR_DEFAULTTONEAREST);
            (settings.MonitorX, settings.MonitorY) = (here.rcMonitor.Center.X, here.rcMonitor.Center.Y);
            if (Dock.OnBar(here, body, Px(24)))
            {
                settings.Along = Dock.AlongOf(here, new POINT(r.Left, r.Top), r.Width, r.Height, Px(EndMargin));
                (settings.FreeX, settings.FreeY) = (null, null);
            }
            else (settings.FreeX, settings.FreeY) = (r.Left, r.Top);
            settings.Save();
            slide = true;
        }
        if (settings.FreeX is int x && settings.FreeY is int y)
        {
            var work = MonitorAt(new POINT(x + r.Width / 2, y + r.Height / 2), MONITOR_DEFAULTTONEAREST).rcWork;
            int inset = Pad;   // his padding may hang off the edge, his body may not
            MoveTo(new POINT(Math.Clamp(x, work.Left - inset, work.Right - r.Width + inset), Math.Clamp(y, work.Top - inset, work.Bottom - r.Height + inset)), slide);
            return;
        }
        var monitor = MonitorAt(new POINT(settings.MonitorX ?? 0, settings.MonitorY ?? 0), MONITOR_DEFAULTTOPRIMARY);
        MoveTo(Dock.Place(monitor, settings.Along, r.Width, r.Height, Px(EndMargin), Px((Box - Body) / 2 - Gap)), slide);
    }

    // 3. A slide on a spring, so being put back reads as him walking there.
    void MoveTo(POINT p, bool slide)
    {
        StopSlide();
        if (!slide || !SystemParameters.ClientAreaAnimation) { Put(p); return; }
        var r = Rect();
        slideX = new Spring(r.Left, (170, 24)) { Target = p.X };
        slideY = new Spring(r.Top, (170, 24)) { Target = p.Y };
        slideLast = 0;
        sliding = true;
        CompositionTarget.Rendering += Slide;
    }

    void Slide(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds, dt = slideLast > 0 ? now - slideLast : 0.016;
        slideLast = now;
        bool moving = slideX!.Step(dt) | slideY!.Step(dt);
        bool far = Math.Abs(slideX.V - slideX.Target) > 0.5 || Math.Abs(slideY.V - slideY.Target) > 0.5;
        if (moving && far) { Put(new POINT((int)Math.Round(slideX.V), (int)Math.Round(slideY.V))); return; }
        StopSlide();
        Put(new POINT((int)slideX.Target, (int)slideY.Target));
    }

    void StopSlide()
    {
        CompositionTarget.Rendering -= Slide;
        sliding = false;
    }

    // 4. He and the chat move in one go, so the chat never draws a frame behind him.
    void Put(POINT p)
    {
        if (!open) { Native.Place(hwnd, p); return; }
        var r = Rect();
        var body = new RECT { Left = p.X, Top = p.Y, Right = p.X + r.Width, Bottom = p.Y + r.Height }.Inset(Pad);
        PlaceTogether((hwnd, p), chat.Spot(body, MonitorAt(body.Center, MONITOR_DEFAULTTONEAREST), Scale));
    }

    ////////// 5. CLICKS AND DRAGS //////////

    void OnPress(object sender, MouseButtonEventArgs e)
    {
        StopSlide();
        pressed = true;
        dragging = false;
        GetCursorPos(out grab);
        grabRect = Rect();
        lastCursor = grab;
        lastMoveAt = clock.Elapsed.TotalSeconds;
        velocity = default;
        CaptureMouse();
    }

    void OnDrag(object sender, MouseEventArgs e)
    {
        if (!pressed) return;
        GetCursorPos(out var c);
        if (!dragging && Math.Abs(c.X - grab.X) + Math.Abs(c.Y - grab.Y) < Px(5)) return;   // wobble on a click is still a click
        if (!dragging) { dragging = true; him.React("grabbed"); }
        Put(new POINT(grabRect.Left + c.X - grab.X, grabRect.Top + c.Y - grab.Y));

        // Smoothed, so one jerky event is not a lurch.
        double now = clock.Elapsed.TotalSeconds;
        velocity = velocity * 0.6 + new Vector(c.X - lastCursor.X, c.Y - lastCursor.Y) / Math.Max(0.004, now - lastMoveAt) / Scale * 0.4;
        lastCursor = c;
        lastMoveAt = now;
        him.Carry(velocity);
    }

    void OnRelease()
    {
        if (!pressed) return;
        pressed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!dragging) { Toggle(!open); return; }
        dragging = false;
        him.Carry(null);
        him.React("dropped");
        Park(dropped: true);
        if (open) Raise(hwnd);
    }

    ////////// 6-7. WATCHING //////////

    // 6. Read from the system, so it works across the whole screen.
    void Look()
    {
        if (aside || pressed) return;
        GetCursorPos(out var c);
        if (Overlay.Hand is { } h) c = h;
        if (c.X == seen.X && c.Y == seen.Y)
        {
            if (!asleep && !open && DateTime.Now - movedAt > Drowsy) { asleep = true; him.React("idle"); }
            return;
        }
        seen = c;
        movedAt = DateTime.Now;
        if (asleep) { asleep = false; him.React("wake"); }
        var r = Rect();
        him.Watch(new Point((c.X - (r.Left + r.Width / 2.0)) / Scale, (c.Y - (r.Top + r.Height / 2.0)) / Scale));
    }

    // 7.
    void StepAside(bool on)
    {
        if (on == aside || pressed) return;
        aside = on;
        him.Paused = on;
        if (on) { SetOpen(false); Hide(); }
        else Show();
    }

    ////////// 8. MENU //////////

    ContextMenu BuildMenu()
    {
        static MenuItem Item(string header, Action click) { var i = new MenuItem { Header = header }; i.Click += (_, _) => click(); return i; }
        return new ContextMenu
        {
            Items =
            {
                Item("Open chat", () => Toggle(true)),
                Item("Apps", () => SettingsWindow.Open(settings, chat)),
                Item("Settings", () => SettingsWindow.Open(settings, chat, "Settings")),
                new Separator(),
                Item("Put him back", () =>
                {
                    (settings.Along, settings.MonitorX, settings.MonitorY, settings.FreeX, settings.FreeY) = (1, null, null, null, null);
                    settings.Save();
                    Park(dropped: false, slide: true);
                }),
                new Separator(),
                Item("Quit Onsay", Close),
            },
        };
    }

    ////////// 9. FOLLOWING HIS HAND //////////

    /* 9. The chat closes and he flies below and right of the pointer's tip, or whichever corner fits, never over it. When
          it fades he goes home. Fly does the moving.

          The chat comes back, without taking the keyboard, only if it was open when his pointer first came out, and only
          once his pointer has gone and his turn is over: his pointer rests between steps, and the chat must not flicker
          open in the gaps. A chat that was closed stays closed. Opening or closing it yourself meanwhile is the last word. */
    void Follow()
    {
        if (aside) return;
        bool showing = Overlay.Hand is not null;
        if (showing && !chasing)
        {
            chasing = true;
            if (open) { reopen = true; SetOpen(false); }
            var r = Rect();
            (fly, flyLast) = ((r.Left, r.Top), 0);
            CompositionTarget.Rendering += Fly;
        }
        else if (!showing && chasing)
        {
            chasing = false;
            CompositionTarget.Rendering -= Fly;
            him.Carry(null);
            Park(dropped: false, slide: true);
        }
        if (reopen && !chasing && !chat.Busy)
        {
            reopen = false;
            SetOpen(true, focus: false);
        }
    }

    // The person opening or closing the chat: whatever was waiting to reopen it is forgotten.
    void Toggle(bool on)
    {
        reopen = false;
        SetOpen(on);
    }

    // Eased toward his spot by the time passed each frame, so he glides without overshoot. Held, he stays in the hand.
    void Fly(object? sender, EventArgs e)
    {
        if (Overlay.Hand is not { } h) return;
        var r = Rect();
        if (pressed) { fly = (r.Left, r.Top); return; }
        if (sliding) StopSlide();
        double now = clock.Elapsed.TotalSeconds, dt = flyLast > 0 ? Math.Min(0.05, now - flyLast) : 0.016;
        flyLast = now;
        var work = MonitorAt(h, MONITOR_DEFAULTTONEAREST).rcWork;
        int off = Px(4);   // his window's own padding keeps his body clear of the tip
        double tx = h.X + off + r.Width <= work.Right ? h.X + off : h.X - off - r.Width;
        double ty = h.Y + off + r.Height <= work.Bottom ? h.Y + off : h.Y - off - r.Height;
        double ease = 1 - Math.Exp(-dt * 10);
        var was = fly;
        fly = (fly.x + (tx - fly.x) * ease, fly.y + (ty - fly.y) * ease);
        Put(new POINT((int)Math.Round(fly.x), (int)Math.Round(fly.y)));
        him.Carry(new Vector(fly.x - was.x, fly.y - was.y) / Math.Max(dt, 0.004) / Scale);
    }
}
