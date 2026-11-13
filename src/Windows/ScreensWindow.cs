// ScreensWindow.cs - the screens lingos draw on, shown: a strip of small ones along a to-do, a grid of every screen open
// for the chat's Screens page, and one window, full screen, that shows one to-do's screens full size, one at a time. What
// is open comes from Screens.cs. A lingo has no say in where its screen shows: while the person watches a to-do's work,
// Show your work, its screens show in a window of their own beside it instead, and go back into the chat when they stop.
//
// 1. Strip(session, height, named)          the small screens of one to-do in a row, hidden with none. A click shows
//                                           that one full screen
//    Every(named)                           every screen open, a card each with its title and its to-do, or a line
//                                           saying there are none. A click shows that one full screen
// 2. Open(named, session, screen, follows)  the window: one to-do's screens, from screen, full screen, or with follows
//                                           a window of their own for Show your work
//    Esc, the arrows, Close                 put it away, go through them, close the one showing
// 3. Follow(named, watched), Resync()       Show your work: the screens of a to-do the person watches kept in their own
//                                           window as screens open and close, and Resync when the toggle changes
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
    static readonly SolidColorBrush Blank = Hex("#2A2C38");

    readonly Func<string, string> named;   // a to-do's name by its session, what was asked
    readonly Grid body = new();
    readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    string session = "";   // the to-do whose screens show, "" for those of none
    long screen;           // the one showing full size
    bool follows;          // opened for Show your work, a window of its own, rather than full screen by the person

    ////////// 1. SMALL SCREENS //////////

    // 1. More than fit scroll sideways, the wheel too while over them.
    public static FrameworkElement Strip(string session, double height, Func<string, string> named)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var strip = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        strip.PreviewMouseWheel += (_, e) =>
        {
            if (strip.ScrollableWidth <= 0) return;
            strip.ScrollToHorizontalOffset(strip.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        Keep(row, () => Screens.Of(session), s =>
        {
            var small = Picture(s, height);
            small.Margin = new Thickness(0, 0, 6, 0);
            small.ToolTip = s.Title;
            small.Cursor = Cursors.Hand;
            small.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Open(named, session, s.Id);
            };
            return (small, small, height);
        }, any => strip.Visibility = Shown(any));
        return strip;
    }

    // 1. Two cards to a row on the chat.
    public static FrameworkElement Every(Func<string, string> named)
    {
        var cards = new WrapPanel();
        var none = new TextBlock
        {
            Text = "No screens are open. When an app works in the background, like the browser or a video, what it shows appears here.",
            FontSize = 13, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 8, 8, 0),
        };
        Keep(cards, () => Screens.All, s =>
        {
            var picture = Picture(s, 112);
            picture.Width = 176;
            var card = new StackPanel
            {
                Width = 176, Margin = new Thickness(6), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Children =
                {
                    picture,
                    new TextBlock { Text = s.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(2, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = s.Session.Length > 0 ? named(s.Session) : "Not part of a to-do", FontSize = 11.5, Foreground = Ink3, Margin = new Thickness(2, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            card.MouseLeftButtonUp += (_, _) => Open(named, s.Session, s.Id);
            return (card, picture, 0);
        }, any => none.Visibility = Shown(!any));
        return new StackPanel { Children = { none, cards } };
    }

    /* Screens kept up to date in a panel while it is on screen: made again, by make, when one opens, closes or joins a
       to-do, and only its picture painted again when what one shows changes, as each frame of a video does. make gives
       what goes in the panel, the picture in it and its height for Paint. any says whether there are any. */
    static void Keep(Panel into, Func<IReadOnlyList<Screen>> which, Func<Screen, (UIElement shown, Border picture, double height)> make, Action<bool> any)
    {
        var pictures = new Dictionary<long, (Border picture, double height)>();
        void Draw()
        {
            into.Children.Clear();
            pictures.Clear();
            var all = which();
            any(all.Count > 0);
            foreach (var s in all)
            {
                var (shown, picture, height) = make(s);
                pictures[s.Id] = (picture, height);
                into.Children.Add(shown);
            }
        }
        void Changed(long? id)
        {
            if (id is null) Draw();
            else if (pictures.TryGetValue(id.Value, out var p) && Screens.Get(id.Value) is { } s) Paint(p.picture, s, p.height);
        }
        into.Loaded += (_, _) =>
        {
            Screens.Changed += Changed;
            Draw();
        };
        into.Unloaded += (_, _) => Screens.Changed -= Changed;
        Draw();
    }

    // A screen's picture in a rounded frame, height tall, as wide as its picture is for that, or 16:10 before it has one.
    static Border Picture(Screen s, double height)
    {
        var b = new Border { Height = height, CornerRadius = new CornerRadius(6), BorderBrush = Edge_, BorderThickness = new Thickness(1) };
        Paint(b, s, height);
        return b;
    }

    // What a screen shows now, on a picture of it already made. height 0 leaves its size as it is.
    static void Paint(Border b, Screen s, double height)
    {
        b.Background = Fill(s);
        if (height > 0) b.Width = s.Picture is not null && s.Height > 0 ? height * s.Width / s.Height : height * 1.6;
    }

    // A screen's picture, whole, or a plain grey until its app shows one.
    static Brush Fill(Screen s) => s.Picture is null ? Blank : new ImageBrush(s.Picture) { Stretch = Stretch.Uniform };

    ////////// 2. FULL SCREEN //////////

    // 2. One window at a time: opened again, it shows what was asked for.
    public static void Open(Func<string, string> named, string session, long screen, bool follows = false)
    {
        shown ??= new ScreensWindow(named);
        shown.session = session;
        shown.screen = screen;
        shown.follows = follows;
        if (follows)
        {
            var area = SystemParameters.WorkArea;
            shown.WindowState = WindowState.Normal;
            shown.Width = Math.Min(1000, area.Width * 0.6);
            shown.Height = Math.Min(680, area.Height * 0.7);
            shown.Left = area.Right - shown.Width - 24;
            shown.Top = area.Top + 24;
        }
        else shown.WindowState = WindowState.Maximized;
        shown.Draw();
        shown.Show();
        if (!follows) shown.Activate();
    }

    ////////// 3. SHOW YOUR WORK //////////

    static Action sync = () => { };
    static string putAway = "";   // a to-do whose window the person closed while watching it, left closed until they toggle

    // 3. Kept as screens open and close. A window the person opened full screen is theirs and left alone.
    public static void Follow(Func<string, string> named, Func<string, bool> watched)
    {
        sync = () =>
        {
            var watching = Screens.All.Select(s => s.Session).Distinct().LastOrDefault(s => watched(s) && s != putAway);
            if (shown is { follows: false }) return;
            if (watching is null)
            {
                shown?.Close();
                return;
            }
            if (shown?.session != watching) Open(named, watching, Screens.Of(watching)[0].Id, true);
        };
        Screens.Changed += id =>
        {
            if (id is null) sync();
        };
    }

    // 3. On the UI thread.
    public static void Resync()
    {
        putAway = "";
        sync();
    }

    ScreensWindow(Func<string, string> named)
    {
        this.named = named;
        Title = "Screens";
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        ShowInTaskbar = true;
        Background = Backdrop;
        FontFamily = Ui;
        var head = new DockPanel { Margin = new Thickness(20, 14, 14, 10) };
        head.Children.Add(Right(Dark("", Close, "Close (Esc)")));
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
        Screens.Changed += Changed;
        Closed += (_, _) =>
        {
            Screens.Changed -= Changed;
            if (follows && Screens.Of(session).Count > 0) putAway = session;
            shown = null;
        };
    }

    readonly Dictionary<long, List<(Border picture, double height)>> live = new();   // each picture showing, by its screen

    // 2. Only what one screen shows changed: each picture of it is painted again, and nothing else is made again.
    void Changed(long? id)
    {
        if (id is null) Draw();
        else if (live.TryGetValue(id.Value, out var pictures) && Screens.Get(id.Value) is { } s)
            foreach (var (picture, height) in pictures) Paint(picture, s, height);
    }

    // What a screen shows, kept up to date as it changes.
    Border Live(Screen s, Border picture, double height)
    {
        if (!live.TryGetValue(s.Id, out var pictures)) live[s.Id] = pictures = new();
        pictures.Add((picture, height));
        return picture;
    }

    // 2. One screen as big as the window lets it, the arrows either side, and every screen of the to-do along the bottom.
    //    With none left it goes.
    void Draw()
    {
        body.Children.Clear();
        live.Clear();
        var mine = Screens.Of(session).ToList();
        if (mine.Count == 0)
        {
            Close();
            return;
        }
        int at = Math.Max(0, mine.FindIndex(s => s.Id == screen));
        var s = mine[at];
        screen = s.Id;
        var of = session.Length > 0 ? $"  ·  {named(session)}" : "";
        title.Text = mine.Count > 1 ? $"{s.Title}  ·  {at + 1} of {mine.Count}{of}" : $"{s.Title}{of}";
        var big = Live(s, new Border { Background = Fill(s), Margin = new Thickness(64, 0, 64, 0) }, 0);
        var shelf = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 16) };
        foreach (var one in mine)
        {
            var small = Live(one, Picture(one, 56), 56);
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
        var close = Button("Close this screen", Kind.Quiet, () => Screens.Close(s.Id), "Takes it away. The app may open another as it works");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 12, 20, 16);
        var bottom = new Grid { Children = { shelf, close } };
        var middle = new Grid { Children = { big } };
        if (mine.Count > 1)
        {
            var left = Dark("", () => Step(-1), "The one before (Left)");
            var right = Dark("", () => Step(1), "The next one (Right)");
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
