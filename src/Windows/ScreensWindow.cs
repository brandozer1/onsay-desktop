// ScreensWindow.cs - the screens lingos draw on, shown: a strip of small ones under a to-do, and one window, full screen,
// that shows every screen of one to-do full size, one at a time, or every screen open in a grid, each named with its
// to-do. What is open comes from Screens.cs. A lingo has no say in where its screen shows.
//
// 1. Strip(session, height, named, key)     the small screens of one to-do in a row, kept up to date, hidden with none.
//                                           A click opens that one full size
// 2. Open(named, key, session, screen)      the window: one to-do's screens from screen, or with no session the grid
//    Esc, the arrows, Close                 put it away, go through them, close the one showing
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Onsay.Desktop.ChatWindow;

namespace Onsay.Desktop;

sealed class ScreensWindow : Window
{
    static ScreensWindow? shown;
    static readonly SolidColorBrush Backdrop = Hex("#16171F");
    static readonly SolidColorBrush Dim = Hex("#9A9DB0");
    static readonly SolidColorBrush Blank = Hex("#2A2C38");

    readonly Func<string, string> named;   // a to-do's name by its session, what was asked
    readonly Func<string?> key;            // the prompt key, for closing one
    readonly Grid body = new();
    readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border backToGrid;
    string? session;   // the to-do whose screens show one at a time, or null for the grid
    long screen;       // the one showing full size
    bool fromGrid;     // opened from the grid, so Back goes there

    ////////// 1. THE STRIP //////////

    // 1. Drawn again whenever a screen changes, while it is on screen. More than fit scroll sideways, the wheel too while
    //    over them.
    public static FrameworkElement Strip(string session, double height, Func<string, string> named, Func<string?> key)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var strip = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        strip.PreviewMouseWheel += (_, e) =>
        {
            if (strip.ScrollableWidth <= 0) return;
            strip.ScrollToHorizontalOffset(strip.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        void Draw()
        {
            row.Children.Clear();
            var mine = Screens.Of(session);
            strip.Visibility = Shown(mine.Count > 0);
            foreach (var s in mine)
            {
                var small = Picture(s, height);
                small.Margin = new Thickness(0, 0, 6, 0);
                small.ToolTip = s.Title;
                small.Cursor = Cursors.Hand;
                small.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Open(named, key, session, s.Id);
                };
                row.Children.Add(small);
            }
        }
        void Changed() => Draw();
        strip.Loaded += (_, _) => Screens.Changed += Changed;
        strip.Unloaded += (_, _) => Screens.Changed -= Changed;
        Draw();
        return strip;
    }

    // A screen's picture in a rounded frame, height tall, as wide as its picture is for that, or 16:10 before it has one.
    static Border Picture(Screen s, double height)
    {
        double wide = s.Picture is not null && s.Height > 0 ? height * s.Width / s.Height : height * 1.6;
        return new Border
        {
            Width = wide,
            Height = height,
            CornerRadius = new CornerRadius(6),
            BorderBrush = Edge_,
            BorderThickness = new Thickness(1),
            Background = Fill(s),
        };
    }

    // A screen's picture, whole, or a plain grey until its app shows one.
    static Brush Fill(Screen s) => s.Picture is null ? Blank : new ImageBrush(s.Picture) { Stretch = Stretch.Uniform };

    ////////// 2. THE WINDOW //////////

    // 2. One window at a time: opened again, it shows what was asked for.
    public static void Open(Func<string, string> named, Func<string?> key, string? session = null, long screen = 0)
    {
        shown ??= new ScreensWindow(named, key);
        shown.fromGrid = session is not null && shown.IsVisible && shown.session is null;
        shown.session = session;
        shown.screen = screen;
        shown.Draw();
        shown.Show();
        shown.Activate();
    }

    ScreensWindow(Func<string, string> named, Func<string?> key)
    {
        this.named = named;
        this.key = key;
        Title = "Screens";
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ShowInTaskbar = true;
        Background = Backdrop;
        FontFamily = Ui;
        backToGrid = Dark("", () =>
        {
            session = null;
            Draw();
        }, "Every screen");
        var head = new DockPanel { Margin = new Thickness(20, 14, 14, 10) };
        head.Children.Add(Right(Dark("", Close, "Close (Esc)")));
        head.Children.Add(backToGrid);
        head.Children.Add(title);
        var layout = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        Grid.SetRow(body, 1);
        layout.Children.Add(head);
        layout.Children.Add(body);
        Content = layout;
        Resources.MergedDictionaries.Add(Thin);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Left) Step(-1);
            else if (e.Key == Key.Right) Step(1);
            else return;
            e.Handled = true;
        };
        Screens.Changed += Draw;
        Closed += (_, _) =>
        {
            Screens.Changed -= Draw;
            shown = null;
        };
    }

    // 2. A to-do with no screens left goes back to the grid, and the grid with none says so.
    void Draw()
    {
        body.Children.Clear();
        var mine = session is null ? new List<Screen>() : Screens.Of(session).ToList();
        if (session is not null && mine.Count == 0) session = null;
        backToGrid.Visibility = Shown(session is not null && fromGrid);
        if (session is null) Every();
        else Gallery(mine);
    }

    // 2. Every screen open, each a card with its title and its to-do. A click shows that to-do's screens full size.
    void Every()
    {
        var all = Screens.All;
        title.Text = all.Count == 1 ? "1 screen" : $"{all.Count} screens";
        if (all.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "No screens are open. When an app works in the background, like the browser, what it shows appears here.", Foreground = Dim, FontSize = 14, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var cards = new WrapPanel { Margin = new Thickness(14, 0, 14, 14) };
        foreach (var s in all)
        {
            var picture = Picture(s, 220);
            picture.Width = 352;
            picture.BorderThickness = new Thickness(0);
            var card = new StackPanel
            {
                Margin = new Thickness(6, 6, 6, 10), Width = 352, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Children =
                {
                    picture,
                    new TextBlock { Text = s.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = s.Session.Length > 0 ? named(s.Session) : "Not part of a to-do", Foreground = Dim, FontSize = 12, Margin = new Thickness(2, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            card.MouseLeftButtonUp += (_, _) =>
            {
                fromGrid = true;
                session = s.Session;
                screen = s.Id;
                Draw();
            };
            cards.Children.Add(card);
        }
        body.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    // 2. One screen as big as the window lets it, the arrows either side, and every screen of the to-do along the bottom.
    void Gallery(List<Screen> mine)
    {
        int at = Math.Max(0, mine.FindIndex(s => s.Id == screen));
        var s = mine[at];
        screen = s.Id;
        title.Text = mine.Count > 1 ? $"{s.Title}  ·  {at + 1} of {mine.Count}  ·  {named(s.Session)}" : $"{s.Title}  ·  {named(s.Session)}";
        if (s.Session.Length == 0) title.Text = s.Title;
        var big = new Border { Background = Fill(s), Margin = new Thickness(64, 0, 64, 0) };
        var shelf = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 16) };
        foreach (var one in mine)
        {
            var small = Picture(one, 56);
            small.Margin = new Thickness(4, 0, 4, 0);
            small.Cursor = Cursors.Hand;
            small.BorderBrush = one.Id == s.Id ? Accent : Brushes.Transparent;
            small.BorderThickness = new Thickness(2);
            small.MouseLeftButtonUp += (_, _) =>
            {
                screen = one.Id;
                Draw();
            };
            shelf.Children.Add(small);
        }
        var close = Button("Close this screen", Kind.Quiet, () => Screens.Close(s.Id, key()), "Takes it away. The app may open another as it works");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 12, 20, 16);
        var bottom = new Grid { Children = { shelf, close } };
        var middle = new Grid { Children = { big } };
        if (mine.Count > 1)
        {
            var left = Dark("", () => Step(-1), "The one before (Left)");
            var right = Dark("", () => Step(1), "The next one (Right)");
            left.Margin = new Thickness(16, 0, 0, 0);
            right.Margin = new Thickness(0, 0, 16, 0);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            middle.Children.Add(left);
            middle.Children.Add(right);
        }
        var layout = new Grid { RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } } };
        Grid.SetRow(bottom, 1);
        layout.Children.Add(middle);
        layout.Children.Add(bottom);
        body.Children.Add(layout);
    }

    // 2. The one before or after, round from the ends.
    void Step(int by)
    {
        if (session is null) return;
        var mine = Screens.Of(session).ToList();
        if (mine.Count < 2) return;
        int at = mine.FindIndex(s => s.Id == screen);
        screen = mine[(at + by + mine.Count) % mine.Count].Id;
        Draw();
    }

    // An icon button that reads on the dark backdrop.
    static Border Dark(string icon, Action click, string tip)
    {
        var b = Button(icon, Kind.Icon, click, tip);
        ((TextBlock)b.Child).Foreground = Brushes.White;
        Hover(b, Hex("#2A2C38"), Brushes.Transparent);
        return b;
    }
}
