// AskWindow.cs - a lingo asking the person first, like "Onsay wants to see your screen", on a small card beside him with
// Allow, Only this time and Don't allow. The lingo client keeps the questions; this asks it for them every second while
// it runs, which is also how it knows someone is here to answer. One question at a time, and the card goes when its
// question does: answered here, or no longer waited for.
//
// 1. Poll()      the questions waiting, the first one shown
// 2. Answer(a)   always, once or no to the one shown
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Onsay.Character;
using static Onsay.Desktop.ChatWindow;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

sealed class AskWindow : Window
{
    const double Wide = 440;   // the card's width in DIPs, its shadow's room around it
    readonly Func<RECT> body;  // his body in real pixels, to sit beside
    readonly TextBlock words = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock from = Note("");
    readonly HashSet<double> answered = new();
    double? shown;             // the id of the question on the card, null when none
    bool asking;

    public AskWindow(Func<RECT> body)
    {
        this.body = body;
        Bare(this);
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI");
        from.Margin = new Thickness(0, 2, 0, 0);
        var face = new OnsayView { Width = 36, Height = 36, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(face, System.Windows.Controls.Dock.Left);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (text, kind, answer) in new[] { ("Allow", Kind.Primary, "always"), ("Only this time", Kind.Quiet, "once"), ("Don't allow", Kind.Quiet, "no") })
        {
            var b = Button(text, kind, () => Answer(answer));
            b.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(b);
        }
        Content = new Border
        {
            Width = Wide - Shadow * 2, Margin = new Thickness(Shadow), Padding = new Thickness(16, 14, 16, 16),
            Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.18 },
            Child = new StackPanel { Children = { new DockPanel { Children = { face, new StackPanel { Children = { words, from } } } }, buttons } },
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Poll();
        timer.Start();
    }

    /* 1. Asked off this thread, since the lingo client answers one request at a time. Windows is his own hands, so it asks
          as Onsay; any other lingo asks by its name, with the file it wants when it wants one. */
    async void Poll()
    {
        if (asking) return;
        asking = true;
        var waiting = Core.Running ? (await Task.Run(() => Core.Control("asks"))).answer as JsonArray : null;
        asking = false;
        if (waiting is null && Core.Running) return;   // a moment it didn't answer: the card stays as it is
        var ask = waiting?.FirstOrDefault(a => (double?)a?["id"] == shown) ?? waiting?.FirstOrDefault(a => !answered.Contains((double?)a?["id"] ?? 0));
        if (ask is null) { shown = null; Hide(); return; }
        if (shown != (double?)ask["id"])
        {
            shown = (double?)ask["id"];
            var (need, path, caller) = ($"{ask["need"]}", ask["path"] is { } p ? $"{p}" : null, $"{ask["caller"]}");
            var wants = Core.Needs.FirstOrDefault(n => n.Name == need).Wants ?? need;
            words.Text = $"{($"{ask["lingo"]}" is var lingo && lingo == "windows" ? "Onsay" : lingo)} wants to {(path is null ? wants : wants.Replace("your files", path))}";
            from.Text = caller == "this computer" ? "" : $"Asked for by {caller.Replace("ip ", "someone at ")}.";
            from.Visibility = caller == "this computer" ? Visibility.Collapsed : Visibility.Visible;
            Show();
        }
        // Beside him, wherever he is now.
        UpdateLayout();
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var at = body();
        var spot = Dock.Beside(MonitorAt(at.Center, MONITOR_DEFAULTTONEAREST), at, (int)Math.Round(ActualWidth * scale), (int)Math.Round(ActualHeight * scale), (int)Math.Round((6 - Shadow) * scale), null, out _);
        Place(new System.Windows.Interop.WindowInteropHelper(this).Handle, spot);
    }

    // 2. The card goes at once, and the next question, if any, comes at the next poll.
    void Answer(string answer)
    {
        if (shown is not { } id) return;
        answered.Add(id);
        shown = null;
        Hide();
        Task.Run(() => Core.Control("answer", new() { ["id"] = id, ["answer"] = answer }));
    }
}
