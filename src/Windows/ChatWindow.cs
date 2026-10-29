// ChatWindow.cs - the card that opens beside him: signing in, home, or the chat, and the look every window shares. It
// only draws: each chat is one session of the onsay lingo on this computer, every message goes to its /onsay, which
// works it through with Onsay a step at a time and keeps the session, and what comes back and what the lingos ask is
// shown here. He is its close button (his back has an X while it is open), and Esc closes it too. Let me choose, at the
// bottom, sends decision mode, so each step is a card of ways to pick from, and while he works his steps show as they
// happen, folded under one line once he answers.
//
// 1.  Open(body, monitor, scale, focus)   opens, or follows him, next to his body rect in pixels, growing out of him
// 2.  Dismiss()                           puts the card away, shrinking back into him, and finishes anything half said
// 3.  Stop()                              Stop, or Alt+Shift+O from any thread: the lingo client starts again, which
//                                         ends whatever it was doing
// 4.  SignInBox(done, centre), SignOut()  signing in through the browser, the same box here, in setting him up and in
//                                         Settings, Account, and signing out from there
// 5.  Refresh(), Page()                   the onsay lingo's status, and the page that fits it: sign in, Onsay turned
//                                         off, home or chat
// 6.  Send(), Ask(text)                   your message to /onsay in this chat's session, followed as it runs, and his
//                                         answer or his questions
//     To(method, path, body)              a request to /onsay, with its prompt key
//     Replied(...)                        his answer, with how it ended and how long it took
// 7.  Working(text)                       the bar: what he is doing right now and for how long, null hides it
// 8.  Tell(text)                          a line across the top, like "Copied the chat."
// 9.  Say(text), Yours(text), His()       his line spoken by the face in the header, drawn as Markdown, and the rows
//                                         of the conversation
// 10. Questions(list, send)               his questions back, each picked or typed, answered together with one Send
//     Asked(waiting), Lingo(kind, text)   a lingo's own: its questions (chat.ask), what it says (chat.say) and a step it
//                                         shows (chat.step)
//     Steps(), AddStep(...), Fold(...)    a message's steps as it runs, each a line, nested ones indented, then folded
// 11. Clear()                             a new chat: a new session, and home again
// 12. Menu()                              the header's menu: Copy chat, from the session, and Apps and settings
// 13. The look every window shares        Brand and the colour tokens, Hex, and the pieces: Button, Paragraph, Label,
//                                         Note, Toggle, Switch, Selectable and the Thin scrollbar
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Onsay.Character;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Onsay.Desktop;

sealed class ChatWindow : Window
{
    ////////// SIZES AND ICONS //////////

    public const double Shadow = 16;   // transparent room around the card for its shadow, in DIPs
    // Glyphs from Icons.
    const string NewIcon = "", MoreIcon = "", SendIcon = "";

    ////////// STATE //////////

    readonly Settings settings;
    readonly Action<bool> setOpen;
    readonly OnsayView face = new() { Width = 32, Height = 32 };
    readonly TextBlock subtitle = new() { FontSize = 12, Foreground = Ink3, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border fresh, send;                   // the header's new chat, and send

    // The pages. Only one shows at a time, see Page.
    readonly FrameworkElement signInPage, offPage, homePage;
    readonly ScrollViewer chatPage = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel lines = new() { Margin = new Thickness(16, 14, 16, 6) };
    readonly TextBlock ready = new() { FontSize = 12, Foreground = Ink3 };
    readonly Ellipse readyDot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0) };

    // The banner across the top, the bar while he works, the box, and the line at the bottom.
    readonly Border banner = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(14, 8, 14, 8), Background = Butter };
    readonly TextBlock bannerText = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Ink2 };
    readonly DockPanel working = new() { Margin = new Thickness(14, 6, 14, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock workingText = new() { FontSize = 12, Foreground = Ink2, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock workingTime = new() { FontSize = 12, FontFamily = Mono, Foreground = Ink3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    readonly Ellipse[] dots = Enumerable.Range(0, 3).Select(_ => new Ellipse { Width = 6, Height = 6, Fill = Accent, Margin = new Thickness(0, 0, 4, 0), Opacity = 0.3 }).ToArray();
    readonly FrameworkElement input, footer;
    readonly TextBox box = new()
    {
        BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 14, Foreground = Ink, VerticalContentAlignment = VerticalAlignment.Center,
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 30, MaxHeight = 116, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    readonly TextBlock hint = new() { FontSize = 14, Foreground = Ink4, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };

    // The onsay lingo's status: {signed_in, signing_in, failed, running, connected, handle, device, key}. Null until it
    // first answers.
    JsonObject? status;
    bool refreshing;
    string? session;                             // this chat's session in the onsay lingo, named here as the onsay lingo names one
    string? promptKey;                           // the onsay lingo's prompt key, read again for each message
    int chat;                                    // which chat this is, so an answer to one cleared away is dropped
    bool waiting, stopping;                      // a message is out, and Stop was pressed while it was
    bool greeted, following = true;
    DateTime doingSince;                         // when what he is doing now started, for the bar's timer
    (RichTextBox row, string text)? speaking;
    CancellationTokenSource? signing;            // a sign in waiting on the browser
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    // Each message's steps: its block in the chat, the line it folds under, its lines, how many and how long it took.
    sealed class StepList { public required Border Root; public required TextBlock Head; public required StackPanel Body; public int Count; public double Ms; }
    readonly List<StepList> stepLists = new();
    // The cards of the questions a lingo waits on, by their id in the lingo client (chat.ask).
    readonly Dictionary<double, Border> lingoCards = new();
    // A prompt waits on Onsay, up to 5 minutes in the onsay lingo.
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(6) };

    public bool Busy => waiting;                    // a message sent from here is being answered
    bool Showing => IsVisible && !closing;          // open, and not on its way out
    bool SignedIn => (bool?)status?["signed_in"] == true;
    bool Off => (bool?)status?["running"] == false;

    readonly Grid whole;                            // the card and its shadow, which open and close as one
    readonly ScaleTransform zoom = new(1, 1);
    Side? side;                                     // where it sits beside him while open, kept so it never flips
    bool closing;                                   // fading out, and hidden once it has

    public ChatWindow(Settings settings, Action<bool> setOpen)
    {
        this.settings = settings;
        this.setOpen = setOpen;
        Native.Bare(this);
        Width = 420 + Shadow * 2;
        Height = 600 + Shadow * 2;
        FontFamily = new FontFamily("Segoe UI");

        // The header: him, who he is and where, and the buttons.
        fresh = Button(NewIcon, Kind.Icon, () => { Clear(); box.Focus(); }, "New chat");
        Border more = null!;
        more = Button(MoreIcon, Kind.Icon, () => Menu(more), "More");
        var header = new DockPanel { Margin = new Thickness(10, 10, 10, 10) };
        header.Children.Add(Right(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { fresh, more } }));
        header.Children.Add(face);
        header.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Onsay", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink }, subtitle },
        });
        banner.Child = bannerText;

        // The sign in page.
        signInPage = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 20),
            Children =
            {
                new OnsayView { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) },
                Paragraph("Sign in so I can help on this computer.", centre: true),
                SignInBox(centre: true),
            },
        };

        // Onsay turned off on this computer: signed in, but nothing to answer.
        var turnOn = Button("Turn Onsay on", Kind.Primary, () => { Core.Control("start", new() { ["lingo"] = "onsay" }); Refresh(); });
        turnOn.HorizontalAlignment = HorizontalAlignment.Center;
        offPage = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 20),
            Children =
            {
                new OnsayView { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) },
                Paragraph("Onsay is turned off on this computer, so I can't answer here. Turn it on and I'm back.", centre: true),
                turnOn,
            },
        };

        // The home page.
        homePage = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(20, 26, 20, 10),
                Children =
                {
                    new OnsayView { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) },
                    new TextBlock { Text = "What can I do for you?", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 22), Children = { readyDot, ready } },
                },
            },
        };

        // The chat page follows new content only while you are at the bottom, so scrolling up to read stays put.
        chatPage.Content = lines;
        chatPage.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) following = chatPage.VerticalOffset >= chatPage.ScrollableHeight - 24;
            else if (following) chatPage.ScrollToEnd();
        };

        // The bar while he works: the dots, what he is doing, for how long, and Stop.
        working.Children.Add(Right(Button("Stop", Kind.Danger, Stop, "Stop him now. Alt+Shift+O does the same from anywhere")));
        working.Children.Add(Right(workingTime));
        var doing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var d in dots) doing.Children.Add(d);
        doing.Children.Add(workingText);
        working.Children.Add(doing);
        tick.Tick += (_, _) => workingTime.Text = Took((DateTime.Now - doingSince).TotalMilliseconds, live: true);

        // The box. Enter sends, Shift+Enter starts a new line, and it grows to a few lines before it scrolls.
        send = Button(SendIcon, Kind.Icon, Send, "Send");
        send.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(send, 1);
        var field = new Border
        {
            BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Margin = new Thickness(12, 8, 12, 8), Padding = new Thickness(10, 3, 3, 3), Background = Card,
            Child = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
                Children = { new Grid { Children = { hint, box } }, send },
            },
        };
        input = field;
        ReadyInput();
        box.TextChanged += (_, _) => ReadyInput();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            Send();
        };
        box.GotKeyboardFocus += (_, _) => field.BorderBrush = Accent;
        box.LostKeyboardFocus += (_, _) => field.BorderBrush = Edge_;

        // The bottom line: what he may do here, Let me choose, and the stop key, written the way a keyboard shortcut is.
        var apps = Button("Apps", Kind.Quiet, () => SettingsWindow.Open(settings, this), "Turn apps on or off, and see what each may do");
        var knob = new Ellipse { Width = 12, Height = 12, Fill = Card, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2), RenderTransform = new TranslateTransform() };
        var track = new Border { Width = 30, Height = 16, CornerRadius = new CornerRadius(8), Child = knob, Margin = new Thickness(0, 0, 6, 0) };
        void Choosing()
        {
            bool on = settings.LetMeChoose == true;
            track.Background = on ? Accent : Edge_;
            ((TranslateTransform)knob.RenderTransform).X = on ? 14 : 0;
        }
        var choose = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            ToolTip = "On: I show you the ways I found, and you pick what I do",
            Children = { track, new TextBlock { Text = "Let me choose", FontSize = 12, Foreground = Ink2, VerticalAlignment = VerticalAlignment.Center } },
        };
        choose.MouseLeftButtonUp += (_, _) => { settings.LetMeChoose = settings.LetMeChoose != true; settings.Save(); Choosing(); };
        Choosing();
        var stopKey = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Stops him from any app, at any time",
            Children =
            {
                new TextBlock { Text = "Stop anytime", FontSize = 11, Foreground = Ink4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                new Border
                {
                    Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 1),
                    Child = new TextBlock { Text = "Alt+Shift+O", FontSize = 10.5, FontFamily = Mono, Foreground = Ink3 },
                },
            },
        };
        footer = new DockPanel { Margin = new Thickness(12, 0, 12, 10), LastChildFill = false, Children = { Right(stopKey), apps, choose } };

        // Everything stacked: the pages share the one row that grows.
        var pages = new Grid { Children = { signInPage, offPage, homePage, chatPage } };
        var layout = new Grid();
        UIElement[] rows = { header, new Border { Height = 1, Background = Rule }, banner, pages, working, input, footer };
        for (int i = 0; i < rows.Length; i++)
        {
            layout.RowDefinitions.Add(new RowDefinition { Height = rows[i] == pages ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetRow(rows[i], i);
            layout.Children.Add(rows[i]);
        }

        /* The card, with its shadow drawn once on a layer of its own behind it. A blurred shadow on the card itself was worked
           out again whenever anything in it moved, his face or the dots, which is what made the card stutter. The two grow
           and fade as one when it opens and closes, see Appear. */
        var shadow = new Border
        {
            Background = Card, CornerRadius = new CornerRadius(14), CacheMode = new BitmapCache(),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 10, Direction = 270, Opacity = 0.18 },
        };
        var card = new Border
        {
            Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Child = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = layout },
        };
        whole = new Grid { Margin = new Thickness(Shadow), Children = { shadow, card }, RenderTransform = zoom };
        Content = whole;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; setOpen(false); } };
        // Alt+F4 puts the card away rather than destroying it. The app closing still closes it.
        Closing += (_, e) => { if (IsVisible && Application.Current.MainWindow?.IsVisible == true) { e.Cancel = true; setOpen(false); } };
        Resources.MergedDictionaries.Add(Thin);
        new WindowInteropHelper(this).EnsureHandle();
        Page();
        Core.Start(settings);
        // Whether this computer is signed in and connected, asked again every few seconds while the card shows.
        var look = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        look.Tick += (_, _) => { if (Showing || status is null) Refresh(); };
        look.Start();
    }

    // The box's hint, and Send only while something is typed and no message is out.
    void ReadyInput()
    {
        hint.Text = Talking ? "Say something" : "Ask for something";
        hint.Visibility = Shown(box.Text.Length == 0);
        bool any = box.Text.Trim().Length > 0 && !Busy;
        send.Opacity = any ? 1 : 0.4;
        send.IsHitTestVisible = any;
    }

    ////////// 1-3. OPEN, CLOSE, STOP //////////

    /* 1. focus false shows it without taking the keyboard, as when he comes back from using the computer. It is put where it
          belongs before it shows, so it never draws a frame where it last was, and then grows out of him. */
    public void Open(Native.RECT body, Native.MONITORINFO monitor, double scale, bool focus)
    {
        bool appearing = !Showing;
        var (hwnd, p) = Spot(body, monitor, scale);
        if (appearing)
        {
            (Left, Top) = (p.X / scale, p.Y / scale);
            ShowActivated = focus;
            Show();
            ShowActivated = true;
            Appear();
            Refresh();
        }
        Native.Place(hwnd, p);
        if (!focus) return;
        Activate();
        box.Focus();
        if (greeted) return;
        greeted = true;
        // He greets you once he has arrived in the header, so the two movements do not cancel out.
        Dispatcher.InvokeAsync(async () => { await Task.Delay(260); face.React("greet"); });
    }

    // Where it goes beside him, without moving it, so he can move both in one go. Its shadow room is most of the gap.
    // While open it keeps to its side for as long as that fits.
    public (IntPtr hwnd, Native.POINT p) Spot(Native.RECT body, Native.MONITORINFO monitor, double scale)
    {
        var p = Dock.Beside(monitor, body, (int)Math.Round(Width * scale), (int)Math.Round(Height * scale), (int)Math.Round((6 - Shadow) * scale),
            Showing ? side : null, out var s);
        side = s;
        return (new WindowInteropHelper(this).Handle, p);
    }

    // 2. It shrinks back into him as it fades, then hides.
    public void Dismiss()
    {
        face.Hush();
        if (speaking is { } s) s.row.Document = Markdown.Render(s.text);
        speaking = null;
        if (!Showing) return;
        closing = true;
        Animate(0, 0.96, 130, EasingMode.EaseIn, () => { if (closing) { closing = false; Hide(); } });
    }

    // Out of the side of him it sits on: from a little smaller and clear to whole, quick and easing out.
    void Appear()
    {
        bool fresh = !closing;   // closing, it turns round from wherever it got to
        closing = false;
        whole.RenderTransformOrigin = side switch
        {
            Side.Above => new Point(0.5, 1), Side.Below => new Point(0.5, 0), Side.Left => new Point(1, 0.5), _ => new Point(0, 0.5),
        };
        if (fresh)
        {
            whole.BeginAnimation(OpacityProperty, null);
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            (whole.Opacity, zoom.ScaleX, zoom.ScaleY) = (0, 0.94, 0.94);
        }
        Animate(1, 1, 190, EasingMode.EaseOut);
    }

    // With Windows' animations turned off, it simply appears and goes.
    void Animate(double opacity, double size, int ms, EasingMode mode, Action? done = null)
    {
        var time = new Duration(TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? ms : 0));
        var ease = new CubicEase { EasingMode = mode };
        var fade = new DoubleAnimation(opacity, time) { EasingFunction = ease };
        if (done is not null) fade.Completed += (_, _) => done();
        whole.BeginAnimation(OpacityProperty, fade);
        zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(size, time) { EasingFunction = ease });
        zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(size, time) { EasingFunction = ease });
    }

    // 3. Whatever the lingo client is running ends as it starts again, the message waiting here with it.
    public void Stop()
    {
        stopping = true;
        Core.Start(settings);
        Dispatcher.BeginInvoke(() => { if (Busy) Working("Stopping"); });
    }

    ////////// 4-5. SIGNING IN, AND THE PAGES //////////

    /* 4. Sign in asks the onsay lingo, which gives the address to open and the code that page shows, and finishes on its own
          once the person presses Connect there. The code shows here too, so they know it is theirs, with Cancel. done runs
          once signed in. centre lines it up for the chat's own page. */
    public FrameworkElement SignInBox(Action? done = null, bool centre = false)
    {
        var align = centre ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var code = new TextBlock { FontFamily = Mono, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Ink };
        var error = Paragraph("", centre);
        error.Foreground = Bad;
        error.Visibility = Visibility.Collapsed;
        var cancel = Button("Cancel", Kind.Quiet, () => signing?.Cancel());
        cancel.HorizontalAlignment = align;
        var waiting = new StackPanel
        {
            Visibility = Visibility.Collapsed, HorizontalAlignment = align,
            Children =
            {
                new TextBlock { Text = "Your code", FontSize = 12, Foreground = Ink3, HorizontalAlignment = align },
                new Border
                {
                    Background = Page_, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 6, 18, 8), Margin = new Thickness(0, 6, 0, 10), HorizontalAlignment = align, Child = code,
                },
                Paragraph("Your browser opened. Sign in there, and press Connect if it shows this code.", centre),
                cancel,
            },
        };
        Border start = null!;
        start = Button("Sign in", Kind.Primary, async () =>
        {
            if (signing is not null) return;
            signing = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var stop = signing.Token;
            error.Visibility = start.Visibility = Visibility.Collapsed;
            try
            {
                var (got, why) = await Task.Run(() => Core.Onsay("sign_in"));
                if (got is null) throw new Exception(why);
                if (got["url"] is { } url)
                {
                    code.Text = $"{got["code"]}";
                    waiting.Visibility = Visibility.Visible;
                    Native.Open($"{url}");
                    for (JsonNode? now = null; (bool?)now?["signed_in"] != true;)
                    {
                        await Task.Delay(2000, stop);
                        now = (await Task.Run(() => Core.Onsay("status"))).answer;
                        if (now is not null && (bool?)now["signed_in"] != true && (bool?)now["signing_in"] != true)
                            throw new Exception(now["failed"] is { } failed ? $"{failed}." : "Signing in didn't finish. Try again.");
                    }
                }
                Refresh();
                face.React("greet");
                done?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { error.Text = e.Message; error.Visibility = Visibility.Visible; }
            finally
            {
                signing = null;
                waiting.Visibility = Visibility.Collapsed;
                start.Visibility = Visibility.Visible;
            }
        });
        start.HorizontalAlignment = align;
        return new StackPanel { Children = { start, waiting, error } };
    }

    // 4. Why it couldn't, or null. The lingo client runs on, for this computer alone.
    public string? SignOut()
    {
        var why = Core.Onsay("sign_out").why;
        status = null;
        Clear();
        Refresh();
        return why;
    }

    // 5. A moment it doesn't answer keeps what was known. Not asked while the lingo client isn't running.
    async void Refresh()
    {
        if (refreshing || !Core.Running) return;
        refreshing = true;
        if ((await Task.Run(() => Core.Onsay("status"))).answer is JsonObject now) status = now;
        refreshing = false;
        Page();
    }

    // 5. Home until something is said here, and while the onsay lingo hasn't answered yet, getting ready.
    bool Talking => lines.Children.Count > 0;

    void Page()
    {
        bool known = status is not null, on = SignedIn && !Off, talking = on && Talking, connected = (bool?)status?["connected"] == true;
        signInPage.Visibility = Shown(known && !SignedIn);
        offPage.Visibility = Shown(SignedIn && Off);
        homePage.Visibility = Shown(!known || (on && !talking));
        chatPage.Visibility = fresh.Visibility = Shown(talking);
        input.Visibility = footer.Visibility = Shown(on);
        subtitle.Text = !known ? "" : SignedIn ? $"{status!["handle"]}" : "Not signed in";
        if (known && !SignedIn) Working(null);
        ReadyInput();
        readyDot.Fill = connected ? Good : Ink4;
        ready.Text = connected ? $"You're {status!["handle"]}. This computer is {status["device"]}." : SignedIn ? "Connecting to Onsay" : "Getting this computer ready";
    }

    ////////// 6. TALKING TO HIM //////////

    // 6. One message at a time: Send waits while one is out.
    void Send()
    {
        var text = box.Text.Trim();
        if (text.Length == 0 || Busy || !SignedIn || Off) return;
        box.Clear();
        Ask(text);
    }

    /* 6. Shown as yours at once, then sent to the onsay lingo's /onsay with its prompt key, in this chat's session, made
          here for the first in the onsay lingo's own way so it can be followed from the start, and with Let me choose on,
          in decision mode. While it runs, what it is doing is asked of the session every moment: each new step is a line,
          the newest one on the bar, and what it asks you is a card, answered on the same card. Its answer is his reply,
          or questions back. */
    async void Ask(string text)
    {
        int mine = chat;
        (waiting, stopping) = (true, false);
        Yours(text);
        Page();
        following = true;
        chatPage.ScrollToEnd();
        Working("Sending your message");
        face.React("thinking");
        var started = DateTime.Now;
        JsonObject? answer = null;
        int code = 0;
        session ??= $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x12}{Random.Shared.Next(0x1000000):x6}";
        var steps = Steps();
        int heard = 0;                           // of the steps Onsay sent, how many are lines here
        Border? card = null;
        using var done = new CancellationTokenSource();
        try
        {
            promptKey = $"{(await Task.Run(() => Core.Control("settings", new() { ["lingo"] = "onsay" }))).answer?.AsArray().FirstOrDefault(s => $"{s?["name"]}" == "prompt_key")?["value"]}";
            async void Follow()
            {
                string? asked = null;
                while (!done.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(400, done.Token);
                        using var look = To(HttpMethod.Get, $"/sessions/{session}/live");
                        using var res = await Http.SendAsync(look, done.Token);
                        if (JsonNode.Parse(await res.Content.ReadAsStringAsync(done.Token)) is not JsonObject now || mine != chat) continue;
                        var got = now["steps"] as JsonArray ?? new();
                        for (; heard < got.Count; heard++) AddStep(steps, $"{got[heard]?["text"]}", (int?)got[heard]?["depth"] ?? 0);
                        if (got.Count > 0) Working($"{got[^1]?["text"]}");
                        if (now["questions"] is not JsonArray q) asked = null;
                        else if (q.ToJsonString() != asked)
                        {
                            asked = q.ToJsonString();
                            card = Questions(q, answers => _ = Http.SendAsync(To(HttpMethod.Post, $"/sessions/{session}/answers", new JsonObject { ["answers"] = new JsonArray(answers.Select(a => (JsonNode?)a).ToArray()) })));
                        }
                    }
                    catch (Exception) { }
                }
            }
            Follow();
            var body = new JsonObject { ["prompt"] = text, ["session"] = session };
            if (settings.LetMeChoose == true) body["mode"] = "decision";
            using var ask = To(HttpMethod.Post, "", body);
            using var res = await Http.SendAsync(ask);
            code = (int)res.StatusCode;
            answer = JsonNode.Parse(await res.Content.ReadAsStringAsync()) as JsonObject;
        }
        catch (Exception) { }
        done.Cancel();
        if (mine != chat) return;
        waiting = false;
        Working(null);
        if (card is not null) card.IsEnabled = false;
        // The answer has every step, for those that came while this computer was too busy to say.
        if (answer?["steps"] is JsonArray all) for (; heard < all.Count; heard++) AddStep(steps, $"{all[heard]?["text"]}", (int?)all[heard]?["depth"] ?? 0);
        var ms = (DateTime.Now - started).TotalMilliseconds;
        Fold(steps, ms);
        if (stopping) await Replied("Stopped.", "Stopped", ms);
        else if (code != 200)
            await Replied(code == 0 ? "Onsay's helper on this computer isn't answering. It starts again by itself, so try again in a moment."
                : code == 404 ? "Onsay is turned off on this computer, so I can't answer. Turn it on and ask again."
                : $"{answer?["error"] ?? "Something went wrong there. Try again in a moment."}", "Failed", ms);
        else
        {
            session = $"{answer!["session"]}";
            if (answer["questions"] is JsonArray questions) Questions(questions);
            else await Replied($"{answer["text"]}", "Done", ms);
        }
    }

    // 6. A request to the onsay lingo's /onsay, with its prompt key as each message last read it.
    HttpRequestMessage To(HttpMethod method, string path, JsonObject? body = null)
    {
        var req = new HttpRequestMessage(method, $"{Core.Url}/onsay{path}") { Content = body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new("Bearer", promptKey);
        return req;
    }

    // 6. His reply, and under it how it ended, Done, Stopped or Failed, how long it took, and Copy.
    async Task Replied(string text, string ended, double ms)
    {
        face.Mood("calm");
        await Say(text, error: ended == "Failed");
        var under = new DockPanel { Margin = new Thickness(0, -8, 0, 12) };
        if (text.Trim().Length > 0) under.Children.Add(Right(Markdown.Copy(text, "Copy")));
        under.Children.Add(new TextBlock { Text = $"{ended} in {Took(ms)}", FontSize = 11, Foreground = Ink4, VerticalAlignment = VerticalAlignment.Center });
        lines.Children.Add(under);
        face.React(ended == "Done" ? "answered" : ended == "Stopped" ? "no" : "stuck");
    }

    ////////// 7-8. THE BAR AND THE BANNER //////////

    // 7. Each dot fades up and down a beat after the one before it. The timer counts from when the current text began.
    void Working(string? text)
    {
        ReadyInput();
        if (text is not null && text != workingText.Text) doingSince = DateTime.Now;
        working.Visibility = Shown(text is not null);
        workingText.Text = text ?? "";
        workingTime.Text = text is null ? "" : Took(0, live: true);
        if (text is null) tick.Stop(); else tick.Start();
        for (int i = 0; i < dots.Length; i++)
            dots[i].BeginAnimation(OpacityProperty, text is null ? null : new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(420))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 160),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    // 8. It goes by itself after a few seconds.
    async void Tell(string text)
    {
        bannerText.Text = text;
        banner.Visibility = Visibility.Visible;
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (bannerText.Text == text) banner.Visibility = Visibility.Collapsed;
    }

    ////////// 9. MESSAGES //////////

    /* 9. The words type out on the same characters his mouth is shaping, drawn as Markdown as they come (Markdown.cs), at
          most a few frames apart. A long reply types faster, so it never takes more than about four seconds. An error reads
          in red. */
    async Task Say(string text, bool error = false)
    {
        var row = His();
        if (error) row.Foreground = Bad;
        speaking = (row, text);
        var drawn = DateTime.MinValue;
        if (Showing) await face.Speak(text, soFar =>
        {
            if ((DateTime.Now - drawn).TotalMilliseconds < 50) return;
            drawn = DateTime.Now;
            row.Document = Markdown.Render(soFar);
        }, Math.Max(46, text.Length / 4.0));
        if (speaking?.row == row) { row.Document = Markdown.Render(text); speaking = null; }
    }

    // Yours sit right on blue, as you typed it.
    void Yours(string text)
    {
        var shown = Selectable(14, Ink);
        shown.Text = text;
        lines.Children.Add(new Border
        {
            Child = shown, Background = BlueSoft, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 7, 12, 8),
            Margin = new Thickness(48, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Right,
        });
    }

    // His sit left on the card, the whole width, read only but selectable, with its links and Copy buttons live.
    RichTextBox His()
    {
        var shown = new RichTextBox
        {
            IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
            Foreground = Ink, Margin = new Thickness(0, 2, 0, 12), Document = new System.Windows.Documents.FlowDocument(),
        };
        lines.Children.Add(shown);
        return shown;
    }

    ////////// HELPERS //////////

    internal static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    static T Right<T>(T e) where T : UIElement
    {
        DockPanel.SetDock(e, System.Windows.Controls.Dock.Right);
        return e;
    }

    internal static void Hover(Border b, Brush over, Brush rest)
    {
        b.MouseEnter += (_, _) => b.Background = over;
        b.MouseLeave += (_, _) => b.Background = rest;
    }

    // How long, the way a person reads it: 0.8s, 12s, 1m 05s. Live, it counts whole seconds.
    static string Took(double ms, bool live = false)
    {
        var s = ms / 1000;
        if (s < 60) return live ? $"{Math.Floor(s)}s" : s < 10 ? $"{s:0.0}s" : $"{Math.Round(s)}s";
        return $"{(int)(s / 60)}m {(int)(s % 60):00}s";
    }

    ////////// 10. QUESTIONS BACK //////////

    /* 10. Each question is text, answered by typing, or {question, choices}, answered by picking one. Send lights up once
           every one is answered, and sends them all as the next message in this session, a line each: the question, then
           its answer. Or, asked while a message runs, to send, its answers in order. Once sent, the card keeps only each
           question and its answer. Gives the card. */
    Border Questions(JsonArray questions, Action<string[]>? send = null)
    {
        var asked = questions.Select(q => q is JsonObject o ? (words: $"{o["question"] ?? o["text"]}", choices: o["choices"] as JsonArray) : (words: $"{q}", choices: null)).ToList();
        var answers = new string[asked.Count];
        var card = new StackPanel();
        Border go = null!;
        void Ready()
        {
            bool all = answers.All(a => !string.IsNullOrWhiteSpace(a));
            go.Opacity = all ? 1 : 0.4;
            go.IsHitTestVisible = all;
        }
        for (int i = 0; i < asked.Count; i++)
        {
            int at = i;
            var label = Label(asked[i].words);
            label.Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 6);
            card.Children.Add(label);
            if (asked[i].choices is { Count: > 0 } choices)
            {
                card.Children.Add(SetupWindow.Chips(choices.Select(c => ($"{c}", $"{c}")).ToArray(), () => answers[at] ?? "", v => { answers[at] = v; Ready(); }));
                continue;
            }
            var typed = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
            typed.TextChanged += (_, _) => { answers[at] = typed.Text; Ready(); };
            card.Children.Add(new Border { BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Background = Card, Child = typed });
        }
        go = Button("Send", Kind.Primary, () =>
        {
            if (send is null && Busy) return;
            var told = asked.Select((q, i) => $"{q.words}{(q.words.TrimEnd().EndsWith('?') || q.words.TrimEnd().EndsWith(':') ? "" : ":")} {answers[i].Trim()}").ToList();
            card.Children.Clear();
            foreach (var line in told) card.Children.Add(new TextBlock { Text = line, FontSize = 13, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            if (send is not null) send(answers.Select(a => a.Trim()).ToArray());
            else Ask(string.Join("\n", told));
        });
        go.Margin = new Thickness(0, 14, 0, 0);
        card.Children.Add(go);
        Ready();
        var shown = new Border
        {
            Child = card, Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 2, 24, 12),
        };
        lines.Children.Add(shown);
        face.Mood("calm");
        face.React("answered");
        return shown;
    }

    /* 10. The questions a lingo waits on the person for (chat.ask), as the lingo client keeps them: each a card here, the
           chat opening for it, its answers sent back to the lingo client in order. A card whose question is no longer
           waited for, answered or given up on, is left as it is and can't be sent. */
    public void Asked(List<JsonNode?> waiting)
    {
        foreach (var (id, card) in lingoCards.Where(c => !waiting.Any(a => (double?)a?["id"] == c.Key)).ToList()) { card.IsEnabled = false; lingoCards.Remove(id); }
        foreach (var a in waiting)
        {
            if ((double?)a?["id"] is not { } id || lingoCards.ContainsKey(id) || a["questions"] is not JsonArray questions) continue;
            if (!Showing) setOpen(true);
            lingoCards[id] = Questions(questions, answers =>
                Task.Run(() => Core.Control("answer", new() { ["id"] = id, ["answer"] = new JsonArray(answers.Select(x => (JsonNode?)x).ToArray()) })));
            Page();
        }
    }

    /* 10. What a lingo says while it works (chat.say), his line in the chat, or a step it shows (chat.step): under the
           message running, a step further in than Onsay's own, and on the bar, else with the steps that came on their
           own. Neither opens the chat, so the work never loses the keyboard. */
    public void Lingo(string kind, string text)
    {
        if (kind == "say") _ = Say(text);
        else
        {
            AddStep(stepLists.LastOrDefault(l => l.Ms == 0) ?? Steps(), text, waiting ? 1 : 0);
            if (waiting) Working(text);
        }
        Page();
    }

    ////////// 10. STEPS //////////

    bool StepsOpen => settings.ShowSteps == true;

    // 10. Open while the message runs, under a quiet rule on its left, and shown only once it has a step.
    StepList Steps()
    {
        var head = new TextBlock { FontSize = 12, Foreground = Ink3, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 2) };
        var body = new StackPanel();
        var list = new StepList
        {
            Head = head, Body = body,
            Root = new Border
            {
                BorderBrush = Rule, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, 0, 8, 12),
                Visibility = Visibility.Collapsed, Child = new StackPanel { Children = { head, body } },
            },
        };
        head.MouseLeftButtonUp += (_, _) =>
        {
            settings.ShowSteps = !StepsOpen;
            settings.Save();
            foreach (var l in stepLists) if (l.Ms > 0) Fold(l, l.Ms);
        };
        stepLists.Add(list);
        lines.Children.Add(list.Root);
        return list;
    }

    // 10. One line, a dot then what he is doing, each level of what it asked a step further in.
    void AddStep(StepList list, string text, int depth)
    {
        list.Count++;
        list.Root.Visibility = Visibility.Visible;
        list.Body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(Math.Min(depth, 4) * 14, 2, 0, 2),
            Inlines = { new System.Windows.Documents.Run("●  ") { Foreground = depth > 0 ? Ink4 : Accent, FontSize = 8 }, new System.Windows.Documents.Run(text) },
        });
    }

    // 10. Once answered it folds under one line, ▸ 4 steps · 3.1s, or stays open under ▾, as he was last told.
    void Fold(StepList list, double ms)
    {
        list.Ms = ms;
        if (list.Count == 0) { lines.Children.Remove(list.Root); stepLists.Remove(list); return; }
        list.Head.Visibility = Visibility.Visible;
        list.Head.Text = $"{(StepsOpen ? "▾" : "▸")} {list.Count} step{(list.Count == 1 ? "" : "s")} · {Took(ms)}";
        list.Body.Visibility = Shown(StepsOpen);
    }

    ////////// 11. A NEW CHAT //////////

    // 11. An answer still on its way is dropped when it comes.
    void Clear()
    {
        lines.Children.Clear();
        stepLists.Clear();
        lingoCards.Clear();
        (session, waiting) = (null, false);
        chat++;
        Working(null);
        Page();
    }

    ////////// 12. THE MENU //////////

    // A row in a menu: a title, and a quieter line under it.
    static Border Row(string text, string under, Action click)
    {
        var row = new Border
        {
            Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = text, FontSize = 13, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = under, FontSize = 11, Foreground = Ink4 },
                },
            },
        };
        Hover(row, Page_, Brushes.Transparent);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    // 12. A small menu under the button, closed by clicking anywhere else.
    void Menu(FrameworkElement anchor)
    {
        var rows = new StackPanel();
        var menu = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4,
            Child = new Border
            {
                Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6),
                MinWidth = 220, MaxWidth = 320, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.16 }, Child = rows,
            },
        };
        // The chat as the onsay lingo keeps it in its session: each message, then his reply or his questions.
        if (Talking && session is not null) rows.Children.Add(Row("Copy chat", "The whole chat, as text", async () =>
        {
            menu.IsOpen = false;
            try
            {
                using var res = await Http.SendAsync(To(HttpMethod.Get, $"/sessions/{session}"));
                var turns = JsonNode.Parse(await res.Content.ReadAsStringAsync())?["turns"] as JsonArray ?? new();
                Clipboard.SetText(string.Join("\n\n", turns.Select(t => $"You: {t?["prompt"]}\n\nOnsay: " +
                    (t?["questions"] is JsonArray q ? string.Join("\n", q.Select(x => x is JsonObject o ? $"{o["question"]}" : $"{x}")) : $"{t?["text"]}"))));
                Tell("Copied the chat.");
            }
            catch (Exception) { Tell("The chat couldn't be copied. Try again in a moment."); }
        }));
        rows.Children.Add(Row("Apps and settings", "Your apps, devices, people, activity and account", () => { menu.IsOpen = false; SettingsWindow.Open(settings, this); }));
        menu.IsOpen = true;
    }

    ////////// 13. THE LOOK EVERY WINDOW SHARES //////////

    // brand.css's tokens, light theme. Every window draws with these, through using static Onsay.Desktop.ChatWindow.
    internal const string Brand = "#5865F2";
    internal static readonly SolidColorBrush Card = Hex("#FFFFFF"), Page_ = Hex("#F7F7FA"), Edge_ = Hex("#D8D9E5"), Rule = Hex("#E8E8F0"),
        Ink = Hex("#12131C"), Ink2 = Hex("#4A4D5E"), Ink3 = Hex("#7B7F92"), Ink4 = Hex("#A9ACBC"), DropSoft = Hex("#D9DAEC"),
        BlueSoft = Hex("#EEF0FF"), Accent = Hex(Brand), AccentDeep = Hex("#4752C4"), AccentEdge = Hex("#353DB8"), Good = Hex("#1F9D55"),
        Bad = Hex("#C62A2F"), BadSoft = Hex("#FDECEC"), Butter = Hex("#FFF6D6");
    internal static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets"), Mono = new("Consolas");

    internal static SolidColorBrush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // Every kind of button is the same shape: a rounded rectangle, never a pill.
    internal enum Kind { Primary, Quiet, Danger, Icon }

    /* Darker under the pointer and pressed in while held. The primary one is filled in the brand colour on a hard shadow,
       the way the website's is. An icon button is a glyph from Icons with no border. A click needs both press and
       release on it. */
    internal static Border Button(string text, Kind kind, Action click, string? tip = null)
    {
        bool icon = kind == Kind.Icon, primary = kind == Kind.Primary;
        Brush rest = primary ? Accent : icon ? Brushes.Transparent : Card;
        Brush hover = primary ? AccentDeep : kind == Kind.Danger ? BadSoft : Page_;
        var b = new Border
        {
            Background = rest, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = primary ? AccentEdge : kind == Kind.Danger ? Bad : Edge_,
            BorderThickness = new Thickness(icon ? 0 : 1), Padding = icon ? new Thickness(0) : new Thickness(14, 6, 14, 7),
            Width = icon ? 32 : double.NaN, Height = icon ? 32 : double.NaN,
            Effect = primary ? new DropShadowEffect { BlurRadius = 0, ShadowDepth = 2.5, Direction = 315, Color = AccentEdge.Color, Opacity = 1 } : null,
            Child = new TextBlock
            {
                Text = text, FontSize = icon ? 14 : 13, FontWeight = icon ? FontWeights.Normal : FontWeights.SemiBold,
                FontFamily = icon ? Icons : new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = primary ? Brushes.White : kind == Kind.Danger ? Bad : icon ? Ink2 : Ink,
            },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
        };
        bool down = false;
        void press(bool on) { var s = (ScaleTransform)b.RenderTransform; s.ScaleX = s.ScaleY = on ? 0.95 : 1; }
        Hover(b, hover, rest);
        b.MouseLeave += (_, _) => { down = false; press(false); };
        b.MouseLeftButtonDown += (_, e) => { down = true; press(true); e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) =>
        {
            press(false);
            if (!down) return;
            down = false;
            e.Handled = true;
            click();
        };
        return b;
    }

    // Body text. centre is for a page of its own, like the chat's sign in.
    internal static TextBlock Paragraph(string text, bool centre = false) => new()
    {
        Text = text, FontSize = 13, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, centre ? 16 : 10),
        TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left, HorizontalAlignment = centre ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        MaxWidth = centre ? 320 : double.PositiveInfinity,
    };

    // A heading over a few things, like a setting's name.
    internal static TextBlock Label(string text) => new() { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 6) };

    // A quieter line, under or beside something. Empty, it takes no room, so one waiting to say how something went is unseen.
    internal static TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Style = Unseen };
    static readonly Style Unseen = new(typeof(TextBlock)) { Triggers = { new Trigger { Property = TextBlock.TextProperty, Value = "", Setters = { new Setter(VisibilityProperty, Visibility.Collapsed) } } } };

    // An on or off switch whose knob slides across. A click on it flips it, and nothing under it.
    internal static Border Toggle(bool on, Action<bool> changed)
    {
        var knob = new Ellipse { Width = 16, Height = 16, Fill = Card, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2), RenderTransform = new TranslateTransform() };
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Child = knob };
        void Draw(bool animate)
        {
            track.Background = on ? Accent : Edge_;
            var to = on ? 18.0 : 0;
            var move = (TranslateTransform)knob.RenderTransform;
            if (animate && SystemParameters.ClientAreaAnimation) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() });
            else { move.BeginAnimation(TranslateTransform.XProperty, null); move.X = to; }
        }
        track.MouseLeftButtonUp += (_, e) => { e.Handled = true; on = !on; Draw(true); changed(on); };
        Draw(false);
        return track;
    }

    // A setting that is on or off, as a card: its words on the left and its Toggle on the right. A click anywhere flips it.
    internal static Border Switch(string text, string? under, bool on, Action<bool> changed)
    {
        var track = Toggle(on, changed);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap } } };
        if (under is not null) words.Children.Add(new TextBlock { Text = under, FontSize = 12.5, Foreground = Ink3, Margin = new Thickness(0, 2, 12, 0), TextWrapping = TextWrapping.Wrap });
        var row = new Border
        {
            Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand, Child = new DockPanel { Children = { Right(track), words } },
        };
        Hover(row, Page_, Card);
        row.MouseLeftButtonUp += (_, _) => track.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
        return row;
    }

    // Read only text that can still be selected and copied.
    internal static TextBox Selectable(double size, Brush ink) => new()
    {
        IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
        TextWrapping = TextWrapping.Wrap, FontSize = size, Foreground = ink,
    };

    // A thin rounded scrollbar with no arrows. Horizontal ones never appear in these windows, so they are hidden.
    internal static readonly ResourceDictionary Thin = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse($"""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="10" />
            <Setter Property="MinWidth" Value="10" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                  <Track x:Name="PART_Track" IsDirectionReversed="True">
                    <Track.Thumb>
                      <Thumb>
                        <Thumb.Template>
                          <ControlTemplate TargetType="Thumb">
                            <Border x:Name="bar" Background="{Edge_.Color}" CornerRadius="3" Margin="3,2" />
                            <ControlTemplate.Triggers>
                              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bar" Property="Background" Value="{Ink4.Color}" /></Trigger>
                              <Trigger Property="IsDragging" Value="True"><Setter TargetName="bar" Property="Background" Value="{Ink3.Color}" /></Trigger>
                            </ControlTemplate.Triggers>
                          </ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
            <Style.Triggers>
              <Trigger Property="Orientation" Value="Horizontal"><Setter Property="Visibility" Value="Collapsed" /></Trigger>
            </Style.Triggers>
          </Style>
        </ResourceDictionary>
        """);
}
