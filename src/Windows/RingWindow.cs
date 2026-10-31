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
using static Onsay.Desktop.ChatWindow;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

sealed class RingWindow : Window
{
    // DIPs. Glow leaves room for the glow and ripple.
    const double Pad = 6;
    const double Gap = 10;
    const double Glow = 28;
    const double Him = 40;
    const double Words = 240;

    readonly Canvas canvas = new();
    readonly Border ring, ripple, bubble;
    readonly TextBlock words = new() { FontSize = 13, FontFamily = Ui, TextWrapping = TextWrapping.Wrap, Foreground = Ink };
    readonly OnsayView him = new() { Width = Him, Height = Him };
    readonly DispatcherTimer timeout = new();
    readonly IntPtr hwnd;
    RECT? now;    // the box ringed, in real pixels
    int move;     // bumped by every point, so a newer one takes over from an older slide

    public RingWindow()
    {
        Bare(this);
        Opacity = 0;

        ring = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = Accent,
            Background = new SolidColorBrush(Accent.Color) { Opacity = 0.08 },
            Effect = new DropShadowEffect { Color = Accent.Color, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.7 },
        };
        ripple = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = Accent, RenderTransformOrigin = new Point(0.5, 0.5) };
        var grow = new ScaleTransform();
        ripple.RenderTransform = grow;
        var forever = new Duration(TimeSpan.FromSeconds(1.7));
        ScaleBoth(grow, new DoubleAnimation(1, 1.14, forever) { RepeatBehavior = RepeatBehavior.Forever });
        ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 0, forever) { RepeatBehavior = RepeatBehavior.Forever });
        bubble = new Border
        {
            Background = Card, CornerRadius = new CornerRadius(14, 14, 14, 5), Padding = new Thickness(11, 8, 11, 8),
            BorderBrush = Rule, BorderThickness = new Thickness(1), Child = words,
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
        if (mine == move && say.Length > 0)
        {
            await him.Speak(say, soFar =>
            {
                if (mine == move) words.Text = soFar;
            });
        }
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
        Left = (int)Math.Round(a.Left + (b.Left - a.Left) * t),
        Top = (int)Math.Round(a.Top + (b.Top - a.Top) * t),
        Right = (int)Math.Round(a.Right + (b.Right - a.Right) * t),
        Bottom = (int)Math.Round(a.Bottom + (b.Bottom - a.Bottom) * t),
    };

    // In DIPs from the window's corner. He goes right of the ring if it fits, else left, else below, else above.
    void Layout(RECT box, double bubbleW)
    {
        now = box;
        double k = Scale;

        // The ring.
        double ringLeft = box.Left / k - Pad;
        double ringTop = box.Top / k - Pad;
        double ringWidth = box.Width / k + Pad * 2;
        double ringHeight = box.Height / k + Pad * 2;

        // The screen he has to work with.
        var work = MonitorAt(box.Center, MONITOR_DEFAULTTONEAREST).rcWork;
        double workLeft = work.Left / k;
        double workTop = work.Top / k;
        double workRight = work.Right / k;
        double workBottom = work.Bottom / k;

        // Him and his words together.
        bool talking = bubbleW > 0;
        double groupWidth = Him + (talking ? 7 + bubbleW : 0);
        double groupHeight = Math.Max(Him, talking ? bubble.DesiredSize.Height : 0);
        bool onRight = ringLeft + ringWidth + Gap + groupWidth <= workRight - 8;
        bool onLeft = !onRight && ringLeft - Gap - groupWidth >= workLeft + 8;

        double groupX = ringLeft;
        double groupY = ringTop + ringHeight + Gap;
        if (onRight) groupX = ringLeft + ringWidth + Gap;
        if (onLeft) groupX = ringLeft - Gap - groupWidth;
        if (onRight || onLeft) groupY = ringTop + ringHeight / 2 - groupHeight / 2;
        else if (groupY + groupHeight > workBottom - 8) groupY = ringTop - Gap - groupHeight;   // no room below: above
        groupX = Math.Clamp(groupX, workLeft + 8, Math.Max(workLeft + 8, workRight - 8 - groupWidth));
        groupY = Math.Clamp(groupY, workTop + 8, Math.Max(workTop + 8, workBottom - 8 - groupHeight));

        // The window, around all of it.
        double left = Math.Min(ringLeft, groupX) - Glow;
        double top = Math.Min(ringTop, groupY) - Glow;
        double right = Math.Max(ringLeft + ringWidth, groupX + groupWidth) + Glow;
        double bottom = Math.Max(ringTop + ringHeight, groupY + groupHeight) + Glow;
        var corner = new POINT((int)Math.Round(left * k), (int)Math.Round(top * k));
        Place(hwnd, corner, ((int)Math.Ceiling((right - left) * k), (int)Math.Ceiling((bottom - top) * k)));
        Width = right - left;
        Height = bottom - top;

        foreach (var b in new[] { ring, ripple })
        {
            Canvas.SetLeft(b, ringLeft - left);
            Canvas.SetTop(b, ringTop - top);
            b.Width = ringWidth;
            b.Height = ringHeight;
        }

        // He sits nearest the ring: before his words on the left, after them otherwise.
        double himX = onLeft ? groupX + groupWidth - Him : groupX;
        Canvas.SetLeft(him, himX - left);
        Canvas.SetTop(him, groupY + groupHeight - Him - top);
        Canvas.SetLeft(bubble, (onLeft ? groupX : groupX + Him + 7) - left);
        Canvas.SetTop(bubble, groupY - top);
        bubble.CornerRadius = onLeft ? new CornerRadius(14, 14, 5, 14) : new CornerRadius(14, 14, 14, 5);
        him.Watch(new Point(ringLeft + ringWidth / 2 - (himX + Him / 2), ringTop + ringHeight / 2 - (groupY + groupHeight - Him / 2)));
    }
}
