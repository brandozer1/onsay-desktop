// RingWindow.cs - a click through glowing ring around something on screen, with a small Onsay beside it saying what it is.
//
// 1. Point(target, say, seconds)   rings a box on screen, in real pixels, and has him say say beside it
// 2. Clear()                       fades it away
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Onsay.Character;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

sealed class RingWindow : Window
{
    const double Pad = 6, Gap = 10, Glow = 28, Him = 40, Words = 240;   // DIPs. Glow leaves room for the glow and ripple
    static readonly Color Blue = Color.FromRgb(0x58, 0x65, 0xF2);

    readonly Canvas canvas = new();
    readonly Border ring, ripple, bubble;
    readonly TextBlock words = new() { FontSize = 13, FontFamily = new FontFamily("Segoe UI"), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x22, 0x33)) };
    readonly OnsayView him = new() { Width = Him, Height = Him };
    readonly DispatcherTimer timeout = new();
    readonly IntPtr hwnd;
    RECT? now;    // the box ringed, in real pixels
    int move;     // bumped by every point, so a newer one takes over from an older slide

    public RingWindow()
    {
        Bare(this);
        Opacity = 0;

        var blue = new SolidColorBrush(Blue);
        ring = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = blue,
            Background = new SolidColorBrush(Color.FromArgb(0x14, Blue.R, Blue.G, Blue.B)),
            Effect = new DropShadowEffect { Color = Blue, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.7 },
        };
        ripple = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = blue, RenderTransformOrigin = new Point(0.5, 0.5) };
        var grow = new ScaleTransform();
        ripple.RenderTransform = grow;
        var forever = new Duration(TimeSpan.FromSeconds(1.7));
        grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.14, forever) { RepeatBehavior = RepeatBehavior.Forever });
        grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.14, forever) { RepeatBehavior = RepeatBehavior.Forever });
        ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 0, forever) { RepeatBehavior = RepeatBehavior.Forever });
        bubble = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(14, 14, 14, 5), Padding = new Thickness(11, 8, 11, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), BorderThickness = new Thickness(1), Child = words,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.16 },
        };
        him.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 4, Opacity = 0.22 };
        canvas.Children.Add(ripple);
        canvas.Children.Add(ring);
        canvas.Children.Add(bubble);
        canvas.Children.Add(him);
        Content = canvas;

        hwnd = ClickThrough(this);
        timeout.Tick += (_, _) => Clear();
        Show();
    }

    double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    // 1. Already showing, it slides from the old box to the new one over 200 ms, so it never jumps.
    public async void Point(RECT target, string say, double seconds)
    {
        int mine = ++move;
        timeout.Stop();
        timeout.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 600));
        timeout.Start();
        bubble.Visibility = say.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        words.Text = say;
        bubble.Width = double.NaN;
        bubble.Measure(new Size(Words, double.PositiveInfinity));
        bubble.Width = say.Length > 0 ? bubble.DesiredSize.Width : 0;   // fixed at its full size, so typing never moves it
        words.Text = "";

        var from = Opacity > 0.05 ? now : null;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(150))));
        for (int i = 1, frames = from is null ? 1 : 12; i <= frames && mine == move; i++)
        {
            double t = (1 - Math.Cos(Math.PI * i / frames)) / 2;
            Layout(from is { } f ? Between(f, target, t) : target, bubble.Width);
            if (frames > 1) await Task.Delay(16);
        }
        if (mine == move && say.Length > 0) await him.Speak(say, s => { if (mine == move) words.Text = s; });
    }

    // 2.
    public void Clear()
    {
        move++;
        timeout.Stop();
        him.Hush();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(300))));
    }

    static RECT Between(RECT a, RECT b, double t) => new()
    {
        Left = (int)Math.Round(a.Left + (b.Left - a.Left) * t), Top = (int)Math.Round(a.Top + (b.Top - a.Top) * t),
        Right = (int)Math.Round(a.Right + (b.Right - a.Right) * t), Bottom = (int)Math.Round(a.Bottom + (b.Bottom - a.Bottom) * t),
    };

    // In DIPs from the window's corner. He goes right of the ring if it fits, else left, else below, else above.
    void Layout(RECT box, double bubbleW)
    {
        now = box;
        double k = Scale;
        double rl = box.Left / k - Pad, rt = box.Top / k - Pad, rw = box.Width / k + Pad * 2, rh = box.Height / k + Pad * 2;
        var work = MonitorAt(box.Center, MONITOR_DEFAULTTONEAREST).rcWork;
        double wl = work.Left / k, wt = work.Top / k, wr = work.Right / k, wb = work.Bottom / k;
        double gw = Him + (bubbleW > 0 ? 7 + bubbleW : 0), gh = Math.Max(Him, bubbleW > 0 ? bubble.DesiredSize.Height : 0);
        string side = rl + rw + Gap + gw <= wr - 8 ? "right" : rl - Gap - gw >= wl + 8 ? "left" : "below";
        double gx = side == "right" ? rl + rw + Gap : side == "left" ? rl - Gap - gw : rl;
        double gy = side == "below" ? rt + rh + Gap : rt + rh / 2 - gh / 2;
        if (side == "below" && gy + gh > wb - 8) gy = rt - Gap - gh;   // no room below: above
        gx = Math.Clamp(gx, wl + 8, Math.Max(wl + 8, wr - 8 - gw));
        gy = Math.Clamp(gy, wt + 8, Math.Max(wt + 8, wb - 8 - gh));

        double left = Math.Min(rl, gx) - Glow, top = Math.Min(rt, gy) - Glow;
        double right = Math.Max(rl + rw, gx + gw) + Glow, bottom = Math.Max(rt + rh, gy + gh) + Glow;
        Place(hwnd, new POINT((int)Math.Round(left * k), (int)Math.Round(top * k)), ((int)Math.Ceiling((right - left) * k), (int)Math.Ceiling((bottom - top) * k)));
        Width = right - left;
        Height = bottom - top;

        foreach (var b in new[] { ring, ripple }) { Canvas.SetLeft(b, rl - left); Canvas.SetTop(b, rt - top); b.Width = rw; b.Height = rh; }
        // He sits nearest the ring: before his words on the left, after them otherwise.
        double himX = side == "left" ? gx + gw - Him : gx;
        Canvas.SetLeft(him, himX - left);
        Canvas.SetTop(him, gy + gh - Him - top);
        Canvas.SetLeft(bubble, (side == "left" ? gx : gx + Him + 7) - left);
        Canvas.SetTop(bubble, gy - top);
        bubble.CornerRadius = side == "left" ? new CornerRadius(14, 14, 5, 14) : new CornerRadius(14, 14, 14, 5);
        him.Watch(new Point(rl + rw / 2 - (himX + Him / 2), rt + rh / 2 - (gy + gh - Him / 2)));
    }
}
