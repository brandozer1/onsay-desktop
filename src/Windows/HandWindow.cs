// HandWindow.cs - Onsay's own pointer, a click through arrowhead in his blue that the core glides around, see Overlay.cs.
// When the real mouse rides along, Windows' own cursor would sit on top of his, so it is hidden while his shows.
//
// 1. Glide(from, to, ms, bend, real)   the curve the core moves along, drawn every frame the screen draws, by the clock
// 2. Press(times)                      a squeeze and a ripple where he clicked, once for each click
// 3. At, Awake                         where it is, and whether it is showing, for him to watch
// 4. Rest()                            the core stopped: it fades out where it is
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

sealed class HandWindow : Window
{
    const double SpotX = 20, SpotY = 40;                       // where the arrow's tip sits inside the window, in DIPs
    static readonly TimeSpan Idle = TimeSpan.FromSeconds(4);   // still this long, it fades out where it is
    static readonly Brush Outline = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));

    readonly ScaleTransform pulse = new();
    readonly Ellipse ripple = new() { Width = 28, Height = 28, StrokeThickness = 2.5, Stroke = Outline, Opacity = 0 };
    readonly DispatcherTimer idle;
    readonly IntPtr hwnd;
    readonly Stopwatch clock = new();
    (POINT from, POINT to, double ms, double cx, double cy)? trip;   // the glide under way, drawn each frame
    TaskCompletionSource? arrived;
    bool hiding;   // Windows' own cursor is hidden, in real mode

    public HandWindow()
    {
        Bare(this);
        Width = 200;
        Height = 80;
        Opacity = 0;

        // Drawn around the tip at 0,0. Round joins on a thick stroke are what make it soft and cartoonish.
        var arrow = new Path
        {
            Data = Geometry.Parse("M0,0 L0,21 L6,16 L15.5,15.5 Z"), Fill = Brushes.White, Stroke = Outline,
            StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round,
        };
        var hand = new Canvas { Children = { arrow }, RenderTransform = pulse, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.3 } };
        Canvas.SetLeft(hand, SpotX);
        Canvas.SetTop(hand, SpotY);
        Canvas.SetLeft(ripple, SpotX - 14);
        Canvas.SetTop(ripple, SpotY - 14);
        // Above and right of the tip, out of his way below it.
        var hint = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 7, 3),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD9, 0xE5)), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "Alt+Shift+O to stop", FontSize = 11, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromRgb(0x7B, 0x7F, 0x92)) },
        };
        Canvas.SetLeft(hint, SpotX + 14);
        Canvas.SetTop(hint, SpotY - 32);
        Content = new Canvas { Children = { ripple, hint, hand } };

        hwnd = ClickThrough(this);
        idle = new DispatcherTimer { Interval = Idle };
        idle.Tick += (_, _) => { idle.Stop(); Sleep(600); };
        Closed += (_, _) => SystemCursor(show: true);
        Show();
    }

    // 3.
    public POINT At { get; private set; }
    public bool Awake { get; private set; }

    POINT SpotPx { get { var k = VisualTreeHelper.GetDpi(this).DpiScaleX; return new((int)Math.Round(SpotX * k), (int)Math.Round(SpotY * k)); } }

    void Put(POINT p)
    {
        At = p;
        var s = SpotPx;
        Place(hwnd, new POINT(p.X - s.X, p.Y - s.Y));
    }

    void Fade(double to, int ms) => BeginAnimation(OpacityProperty, new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))));

    // Seen again: it fades in where it is, never jumping in from somewhere else.
    void Wake()
    {
        idle.Stop();
        idle.Start();
        if (Awake) return;
        Awake = true;
        Fade(1, 150);
    }

    void Sleep(int ms)
    {
        Awake = false;
        Fade(0, ms);
        SystemCursor(show: true);
    }

    void SystemCursor(bool show)
    {
        if (show == !hiding) return;
        hiding = !show;
        if (show) ShowSystemCursor();
        else HideSystemCursor();
    }

    /* 1. Eased along the same quadratic curve, bowed by bend pixels, that the core moves the real mouse along. Placed by the
          clock each frame, so a late frame never makes it stutter. A newer glide takes over mid way. */
    public Task Glide(POINT from, POINT to, int ms, double bend, bool real)
    {
        arrived?.TrySetResult();
        double dx = to.X - from.X, dy = to.Y - from.Y, span = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        trip = (from, to, Math.Max(1, ms), from.X + dx / 2 - dy / span * bend, from.Y + dy / 2 + dx / span * bend);
        arrived = new TaskCompletionSource();
        Put(from);
        Wake();
        SystemCursor(show: !real);
        clock.Restart();
        CompositionTarget.Rendering -= Frame;
        CompositionTarget.Rendering += Frame;
        return arrived.Task;
    }

    void Frame(object? sender, EventArgs e)
    {
        if (trip is not { } g) { CompositionTarget.Rendering -= Frame; return; }
        double k = Math.Min(1, clock.Elapsed.TotalMilliseconds / g.ms), t = (1 - Math.Cos(Math.PI * k)) / 2, u = 1 - t;
        Put(new POINT((int)Math.Round(u * u * g.from.X + 2 * u * t * g.cx + t * t * g.to.X), (int)Math.Round(u * u * g.from.Y + 2 * u * t * g.cy + t * t * g.to.Y)));
        if (k < 1) return;
        trip = null;
        CompositionTarget.Rendering -= Frame;
        Wake();
        arrived?.TrySetResult();
    }

    // 2.
    public async void Press(int times)
    {
        Wake();
        for (int i = 0; i < Math.Max(1, times); i++)
        {
            var quick = new Duration(TimeSpan.FromMilliseconds(110));
            var squeeze = new DoubleAnimation(0.8, quick) { AutoReverse = true };
            pulse.BeginAnimation(ScaleTransform.ScaleXProperty, squeeze);
            pulse.BeginAnimation(ScaleTransform.ScaleYProperty, squeeze);
            var grow = new ScaleTransform(0.4, 0.4, 14, 14);
            ripple.RenderTransform = grow;
            var spread = new DoubleAnimation(0.4, 1.4, new Duration(TimeSpan.FromMilliseconds(380)));
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
            ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, new Duration(TimeSpan.FromMilliseconds(380))));
            if (i + 1 < times) await Task.Delay(160);
        }
    }

    // 4.
    public void Rest()
    {
        trip = null;
        arrived?.TrySetResult();
        idle.Stop();
        Sleep(300);
    }
}
