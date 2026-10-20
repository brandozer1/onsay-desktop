// ChatWindow.cs - the card that opens beside him: the sign in page, home, or the chat. The chat lives only here, in this
// window: each message is run on the server as lingo(words), the chat before it given with it, and nothing of it is kept
// there.
// He is its close button (his back has an X while it is open), and Esc closes it too.
//
// 1.  Open(body, monitor, scale, focus)   opens, or follows him, next to his body rect in pixels, growing out of him
// 2.  Dismiss()                           puts the card away, shrinking back into him, and finishes anything half said
// 3.  Stop()                              Stop, or Alt+Shift+O from any thread: stops his turn and the core at once
// 4.  SignIn(), SignOut()                 through the browser, from the sign in page or his menu
// 5.  Page()                              shows the page that fits: sign in, home or chat
// 6.  Send()                              your message goes to the server with an id, and its reply comes back with it
//     Heard(m), Replied(...)              what the server says, drawn here as it happens, and his reply
// 7.  Working(text)                       the bar: what he is doing right now and for how long, null hides it
// 8.  Tell(text, bad)                     a line across the top, for what happens outside the chat
// 9.  Say(text), Message(mine)            his line spoken by the face in the header, and one row of the conversation
// 10. Button(text, kind, click)           every button in the card, one shape
// 11. Steps(), AddStep(list, done)        a message's steps on other computers, folded under his reply, each opening to
//                                         its IN and OUT
// 12. Clear()                             a new chat: the conversation here forgotten, and home again
// 13. Menu(), Permissions()               the menus
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
using Run = System.Windows.Documents.Run;

namespace Onsay.Desktop;

sealed class ChatWindow : Window
{
    ////////// LOOK //////////

    // brand.css tokens, light theme.
    static readonly Brush Card = Hex("#FFFFFF"), Page_ = Hex("#F7F7FA"), Edge_ = Hex("#D8D9E5"), Rule = Hex("#E8E8F0"),
        Ink = Hex("#12131C"), Ink2 = Hex("#4A4D5E"), Ink3 = Hex("#7B7F92"), Ink4 = Hex("#A9ACBC"),
        BlueSoft = Hex("#EEF0FF"), Accent = Hex("#5865F2"), AccentDeep = Hex("#4752C4"), Good = Hex("#1F9D55"),
        Bad = Hex("#C62A2F"), BadSoft = Hex("#FDECEC"), Butter = Hex("#FFF6D6");
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets"), Mono = new("Consolas");
    // Glyphs from Icons, by code point.
    static readonly string NewIcon = Glyph(0xE710), MoreIcon = Glyph(0xE712), SendIcon = Glyph(0xE724),
        ClosedIcon = Glyph(0xE76C), OpenIcon = Glyph(0xE70D);
    public const double Shadow = 16;   // transparent room around the card for its shadow, in DIPs
    const int DetailMax = 1500;        // characters of a step's IN or OUT shown, the rest is trimmed

    // Every kind of button is the same shape: a rounded rectangle, never a pill.
    enum Kind { Primary, Quiet, Danger, Icon }

    ////////// STATE //////////

    readonly Settings settings;
    readonly Action<bool> setOpen;
    readonly OnsayView face = new() { Width = 32, Height = 32 };
    readonly TextBlock subtitle = new() { FontSize = 12, Foreground = Ink3, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border fresh, more, permits, send;          // the header's new chat and menu, permissions, and send

    // The pages. Only one shows at a time, see Page.
    readonly FrameworkElement signInPage, homePage;
    readonly ScrollViewer chatPage = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel lines = new() { Margin = new Thickness(16, 14, 16, 6) };
    readonly TextBlock ready = new() { FontSize = 12, Foreground = Ink3 };
    readonly Ellipse readyDot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0) };
    readonly StackPanel codePanel = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock code = new() { FontFamily = Mono, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock signInError = new() { FontSize = 12, Foreground = Bad, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed };
    readonly Border signInButton;

    // The banner across the top, the bar while he works, the box, and the line at the bottom.
    readonly Border banner = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(14, 8, 14, 8) };
    readonly TextBlock bannerText = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
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

    readonly HashSet<string> asking = new();     // the ids of the messages sent from here still waiting for their reply
    string? me;                                  // this computer's instance id, from ready
    string called = Environment.MachineName;     // its route on Onsay, like laptop.brandon, from ready
    bool online;                                 // whether this computer's core is connected, so he can act here
    bool greeted, following = true;
    DateTime doingSince;                         // when what he is doing now started, for the bar's timer
    StepList? turn;                              // the steps of the message in progress, before his next line
    (TextBox row, string text)? speaking;
    CancellationTokenSource? signing;            // a sign in waiting on the browser
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly StringBuilder transcript = new();   // everything said and done, with every step in full, for Copy chat
    readonly List<(string from, string text)> said = new();   // the chat, from you or onsay, oldest first
    const int ChatTurns = 40, ChatText = 20_000; // the most of it that goes with a message: its newest turns that fit, as the
                                                 // server reads them (../onsay/server/search/search.js chat)

    // One accordion per reply. Head is its clickable line, Body its rows, hidden while it has none.
    sealed class StepList { public required Border Root; public required TextBlock Head; public required StackPanel Body; public int Count; public double Ms; }
    // One step on another of the account's computers.
    sealed record Done(string Title, string In, string Out, double Ms);
    readonly List<StepList> stepLists = new();

    public bool Signing => signing is not null;
    public bool Busy => asking.Count > 0;           // a message sent from here is being answered
    bool Showing => IsVisible && !closing;          // open, and not on its way out

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
        more = Button(MoreIcon, Kind.Icon, () => Popup(more!, Menu), "More");
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
        signInButton = Button("Sign in", Kind.Primary, SignIn);
        signInButton.HorizontalAlignment = HorizontalAlignment.Center;
        codePanel.Children.Add(new TextBlock { Text = "Your code", FontSize = 12, Foreground = Ink3, HorizontalAlignment = HorizontalAlignment.Center });
        codePanel.Children.Add(new Border
        {
            Background = Page_, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 6, 18, 8), Margin = new Thickness(0, 6, 0, 10), Child = code,
        });
        codePanel.Children.Add(Paragraph("Your browser opened. Sign in there, and press Connect if it shows this code."));
        var cancel = Button("Cancel", Kind.Quiet, () => signing?.Cancel());
        cancel.HorizontalAlignment = HorizontalAlignment.Center;
        cancel.Margin = new Thickness(0, 14, 0, 0);
        codePanel.Children.Add(cancel);
        signInPage = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 20),
            Children =
            {
                new OnsayView { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) },
                new TextBlock { Text = "Hi, I'm Onsay", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center },
                Paragraph("I help on this computer: tidying files, filling things in, reading what's on screen. Sign in and tell me what you need.", 8, 20),
                signInButton, codePanel, signInError,
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

        // The bottom line: what he may do here, and the stop key.
        permits = Button("", Kind.Quiet, () => Popup(permits!, Permissions), "What Onsay may do on this computer");
        var stopKey = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Stops him from any app, at any time",
            Children =
            {
                new TextBlock { Text = "Stop anytime", FontSize = 11, Foreground = Ink4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                Shortcut("Alt+Shift+O"),
            },
        };
        footer = new DockPanel { Margin = new Thickness(12, 0, 12, 10), Children = { Right(stopKey), new StackPanel { Orientation = Orientation.Horizontal, Children = { permits } } } };
        Labels();

        // Everything stacked: the pages share the one row that grows.
        var pages = new Grid { Children = { signInPage, homePage, chatPage } };
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
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 10, Direction = 270, Opacity = 0.18, Color = Color.FromRgb(15, 17, 21) },
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
        Online();
    }

    // Once signed in: the chat's connection, and the core.
    void Online()
    {
        if (settings.Token is null) return;
        Link.Start(settings, Heard);
        Core.Start(settings);
    }

    // The box's hint, and Send only while something is typed.
    void ReadyInput()
    {
        hint.Text = Talking ? "Say something" : "Ask for something";
        hint.Visibility = Shown(box.Text.Length == 0);
        bool any = box.Text.Trim().Length > 0;
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
        if (speaking is { } s) s.row.Text = s.text;
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

    // 3. Asks the server to stop every message waiting here, and restarts the core so whatever program it is running ends
    //    now, even if the server never hears.
    public void Stop()
    {
        Core.Restart();
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var id in asking) Link.Stop(id);
            if (Busy) Working("Stopping");
        });
    }

    ////////// 4-5. SIGNING IN, AND THE PAGES //////////

    // 4. The code shows here too, so the person knows the browser page is theirs.
    public async void SignIn()
    {
        if (signing is not null) return;
        if (!Showing) setOpen(true);
        signing = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        signInError.Visibility = Visibility.Collapsed;
        signInButton.Visibility = Visibility.Collapsed;
        try
        {
            var who = await Link.Connect(settings, c => Dispatcher.Invoke(() => { code.Text = c; codePanel.Visibility = Visibility.Visible; }), signing.Token);
            Online();
            Page();
            face.React("greet");
            Tell($"Signed in as {who}.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { signInError.Text = $"That didn't work: {e.Message}"; signInError.Visibility = Visibility.Visible; }
        finally
        {
            signing = null;
            codePanel.Visibility = Visibility.Collapsed;
            signInButton.Visibility = Visibility.Visible;
        }
    }

    public void SignOut()
    {
        if (signing is not null) { signing.Cancel(); return; }
        (settings.Token, settings.User) = (null, null);
        settings.Save();
        Link.Close();
        Core.Stop();
        Clear();
    }

    // 5. Home until something is said here.
    bool Talking => lines.Children.Count > 0;

    void Page()
    {
        bool signedIn = settings.Token is not null, talking = signedIn && Talking;
        signInPage.Visibility = Shown(!signedIn);
        homePage.Visibility = Shown(signedIn && !talking);
        chatPage.Visibility = fresh.Visibility = Shown(talking);
        input.Visibility = footer.Visibility = more.Visibility = Shown(signedIn);
        subtitle.Text = !signedIn ? "Not signed in" : settings.User ?? "";
        if (!signedIn) Working(null);
        ReadyInput();
        Readiness();
    }

    // Whether he can act on this computer right now, on the home page.
    void Readiness()
    {
        readyDot.Fill = online ? Good : Ink4;
        ready.Text = online ? $"This computer is {called}" : "Getting this computer ready";
    }

    ////////// 6. TALKING TO HIM //////////

    // 6. Your message shows once it is on its way. Each runs on its own, so one sent while another works waits for nothing.
    async void Send()
    {
        var text = box.Text.Trim();
        if (text.Length == 0 || settings.Token is null) return;
        var id = Guid.NewGuid().ToString("N");
        box.Clear();
        // The chat before it, its oldest turns dropped first, and what was around it: this computer's time, zone and
        // language, and the window the person was in.
        var chat = new JsonArray();
        for (int i = said.Count - 1, room = ChatText; i >= 0 && chat.Count < ChatTurns && (room -= said[i].text.Length) >= 0; i--)
            chat.Insert(0, new JsonObject { ["from"] = said[i].from, ["text"] = said[i].text });
        var (now, zone, (app, window)) = (DateTimeOffset.Now, TimeZoneInfo.Local, Native.InFront());
        var context = new JsonObject
        {
            ["os"] = "windows", ["time"] = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), ["zone"] = zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName,
            ["language"] = System.Globalization.CultureInfo.CurrentUICulture.Name, ["app"] = app, ["window"] = window,
        };
        if (!await Link.Say(id, text, chat, context))
        {
            box.Text = text;
            face.React("stuck");
            Tell("I can't reach my server right now. I'll keep trying, so send it again in a moment.", bad: true);
            return;
        }
        asking.Add(id);
        turn = null;
        Message(true).Text = text;
        Record($"You: {text}");
        said.Add(("you", text));
        Page();
        following = true;
        chatPage.ScrollToEnd();
        Working("Thinking");
        face.React("thinking");
    }

    // Everything the server sends, on this window's thread. Only what answers a message sent from here is drawn.
    void Heard(JsonObject m)
    {
        switch ($"{m["type"]}")
        {
            case "ready":
                (me, online) = ($"{m["instance"]}", (bool?)m["online"] == true);
                if ($"{m["name"]}" is { Length: > 0 } name) called = name;
                Readiness();
                return;
            case "instance":
                if ($"{m["id"]}" == me) { online = (bool?)m["online"] == true; Readiness(); }
                return;
            // A reply can't come back on a connection that dropped, so what was waiting is said to be lost.
            case "offline":
                if (!Busy) return;
                asking.Clear();
                turn = null;
                Working(null);
                Tell("I lost my connection to the server, so that may not have finished. Ask again in a moment.", bad: true);
                return;
            case "error": Tell($"My server couldn't do that: {m["error"]}", bad: true); return;
            case "signedout":
                Core.Stop();
                Clear();
                Tell("This computer was signed out of Onsay.", bad: true);
                return;
            case "step" when asking.Contains($"{m["id"]}"):
                AddStep(turn ??= Steps(), new Done($"{m["title"]}", $"{m["in"]}", $"{m["out"]}", (double?)m["ms"] ?? 0));
                return;
            case "reply" when asking.Remove($"{m["id"]}"):
                _ = Replied($"{m["ended"]}", $"{m["text"]}", (double?)m["ms"] ?? 0);
                return;
        }
    }

    // His reply, with how long it took. ended: answered, stopped or failed.
    async Task Replied(string ended, string text, double ms)
    {
        turn = null;
        if (!Busy) Working(null);
        face.Mood("calm");
        await Say(text, error: ended == "failed");
        lines.Children.Add(new TextBlock { Text = $"{(ended == "answered" ? "Done" : ended == "stopped" ? "Stopped" : "Failed")} in {Took(ms)}", FontSize = 11, Foreground = Ink4, Margin = new Thickness(0, -6, 0, 12) });
        face.React(ended == "answered" ? "answered" : ended == "stopped" ? "no" : "stuck");
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
    public async void Tell(string text, bool bad = false)
    {
        if (!Showing) setOpen(true);
        banner.Background = bad ? BadSoft : Butter;
        bannerText.Foreground = bad ? Bad : Ink2;
        bannerText.Text = text;
        banner.Visibility = Visibility.Visible;
        await Task.Delay(TimeSpan.FromSeconds(bad ? 9 : 5));
        if (bannerText.Text == text) banner.Visibility = Visibility.Collapsed;
    }

    ////////// 9-10. MESSAGES AND BUTTONS //////////

    // 9. The words type out on the same characters his mouth is shaping. An error reads in red.
    async Task Say(string text, bool error = false)
    {
        var row = Message(false);
        if (error) row.Foreground = Bad;
        Record($"Onsay: {text}");
        said.Add(("onsay", text));
        speaking = (row, text);
        if (Showing) await face.Speak(text, soFar => row.Text = soFar);
        if (speaking?.row == row) { row.Text = text; speaking = null; }
    }

    // Yours sit right on blue, his sit left on the card.
    TextBox Message(bool mine)
    {
        var text = Selectable(14, Ink);
        lines.Children.Add(new Border
        {
            Child = text,
            Background = mine ? BlueSoft : Brushes.Transparent,
            CornerRadius = new CornerRadius(12),
            Padding = mine ? new Thickness(12, 7, 12, 8) : new Thickness(0, 2, 0, 2),
            Margin = new Thickness(mine ? 48 : 0, 0, mine ? 0 : 24, 12),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        });
        return text;
    }

    /* 10. Darker under the pointer and pressed in while held. An icon button is a glyph from Windows' icon font with no
           border. A click needs both press and release on it. */
    static Border Button(string text, Kind kind, Action click, string? tip = null)
    {
        bool icon = kind == Kind.Icon;
        Brush rest = kind == Kind.Primary ? Accent : icon ? Brushes.Transparent : Card;
        Brush hover = kind == Kind.Primary ? AccentDeep : kind == Kind.Danger ? BadSoft : Page_;
        var b = new Border
        {
            Background = rest, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = kind == Kind.Primary ? Accent : kind == Kind.Danger ? Bad : Edge_, BorderThickness = new Thickness(icon ? 0 : 1),
            Padding = icon ? new Thickness(0) : new Thickness(12, 5, 12, 6), Width = icon ? 32 : double.NaN, Height = icon ? 32 : double.NaN,
            Child = new TextBlock
            {
                Text = text, FontSize = icon ? 14 : 13, FontWeight = icon ? FontWeights.Normal : FontWeights.SemiBold,
                FontFamily = icon ? Icons : new FontFamily("Segoe UI"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = kind == Kind.Primary ? Brushes.White : kind == Kind.Danger ? Bad : icon ? Ink2 : Ink,
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

    ////////// HELPERS //////////

    static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    static T Right<T>(T e) where T : UIElement
    {
        DockPanel.SetDock(e, System.Windows.Controls.Dock.Right);
        return e;
    }

    static void Hover(Border b, Brush over, Brush rest)
    {
        b.MouseEnter += (_, _) => b.Background = over;
        b.MouseLeave += (_, _) => b.Background = rest;
    }

    // Read only text that can still be selected and copied.
    static TextBox Selectable(double size, Brush ink) => new()
    {
        IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
        TextWrapping = TextWrapping.Wrap, FontSize = size, Foreground = ink,
    };

    // A key, the way a keyboard shortcut is written.
    static Border Shortcut(string text) => new()
    {
        Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 1),
        Child = new TextBlock { Text = text, FontSize = 10.5, FontFamily = Mono, Foreground = Ink3 },
    };

    static TextBlock Paragraph(string text, double above = 6, double below = 0) => new()
    {
        Text = text, FontSize = 13, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, above, 0, below), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Center,
    };

    static TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 6, 10, 6) };

    // How long, the way a person reads it: 0.8s, 12s, 1m 05s. Live, it counts whole seconds.
    static string Took(double ms, bool live = false)
    {
        var s = ms / 1000;
        if (s < 60) return live ? $"{Math.Floor(s)}s" : s < 10 ? $"{s:0.0}s" : $"{Math.Round(s)}s";
        return $"{(int)(s / 60)}m {(int)(s % 60):00}s";
    }

    static string Glyph(int code) => ((char)code).ToString();

    static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    ////////// 11. STEPS //////////

    bool StepsOpen => settings.ShowSteps == true;

    StepList Steps()
    {
        var head = new TextBlock { FontSize = 12, Foreground = Ink3, Cursor = Cursors.Hand };
        var body = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = Shown(StepsOpen) };
        var list = new StepList
        {
            Head = head, Body = body,
            Root = new Border
            {
                BorderBrush = Rule, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, 0, 8, 12),
                Visibility = Visibility.Collapsed, Child = new StackPanel { Children = { head, body } },
            },
        };
        head.MouseLeftButtonUp += (_, _) => ToggleSteps();
        stepLists.Add(list);
        lines.Children.Add(list.Root);
        return list;
    }

    // 11. One line per step with how long it took, opening to what went in and came out.
    void AddStep(StepList list, Done done)
    {
        list.Count++;
        list.Ms += done.Ms;
        list.Root.Visibility = Visibility.Visible;
        Head(list);
        Record($"  {done.Title}{(done.Ms > 0 ? $" ({Took(done.Ms)})" : "")}\n    IN:\n{Indent(done.In)}\n    OUT:\n{Indent(done.Out)}");
        bool failed = done.Out.StartsWith("error") || done.Out.StartsWith("It did not");
        var chevron = new TextBlock { Text = ClosedIcon, FontFamily = Icons, FontSize = 9, Foreground = Ink4, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { LastChildFill = true };
        line.Children.Add(Right(chevron));
        line.Children.Add(Right(new TextBlock { Text = done.Ms > 0 ? Took(done.Ms) : "", FontSize = 11, FontFamily = Mono, Foreground = Ink4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }));
        line.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Ink2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Inlines = { new Run("●  ") { Foreground = failed ? Bad : Accent, FontSize = 8 }, new Run(done.Title) },
        });
        var header = new Border { Child = line, Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Hover(header, Page_, Brushes.Transparent);

        var body = new Border
        {
            Child = new StackPanel { Children = { Block("IN", done.In), Block("OUT", done.Out) } },
            Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 8), Margin = new Thickness(4, 2, 0, 6), Visibility = Visibility.Collapsed,
        };
        header.MouseLeftButtonUp += (_, _) =>
        {
            bool open = body.Visibility != Visibility.Visible;
            body.Visibility = Shown(open);
            chevron.Text = open ? OpenIcon : ClosedIcon;
        };
        list.Body.Children.Add(new StackPanel { Children = { header, body } });
    }

    void ToggleSteps()
    {
        settings.ShowSteps = !StepsOpen;
        settings.Save();
        foreach (var list in stepLists)
        {
            list.Body.Visibility = Shown(StepsOpen);
            Head(list);
        }
    }

    void Head(StepList list) =>
        list.Head.Text = $"{(StepsOpen ? "Hide" : "Show")} {list.Count} step{(list.Count == 1 ? "" : "s")}{(list.Ms > 0 ? $" · {Took(list.Ms)}" : "")}";

    static UIElement Block(string label, string text)
    {
        text = text.Trim();
        var shown = Selectable(11, Ink2);
        shown.FontFamily = Mono;
        shown.Text = text.Length == 0 ? "(nothing)" : text.Length > DetailMax ? text[..DetailMax].TrimEnd() + "\n(trimmed)" : text;
        return new StackPanel
        {
            Margin = new Thickness(0, 2, 0, 4),
            Children =
            {
                new TextBlock { Text = label, FontFamily = Mono, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Ink3, Margin = new Thickness(0, 0, 0, 2) },
                shown,
            },
        };
    }

    void Record(string line) => transcript.AppendLine(line).AppendLine();

    static string Indent(string text) => string.Join("\n", (text.Trim().Length == 0 ? "(nothing)" : text.Trim()).Split('\n').Select(l => "      " + l.TrimEnd('\r')));

    ////////// 12. A NEW CHAT //////////

    // 12. Replies still on their way are dropped when they come.
    void Clear()
    {
        lines.Children.Clear();
        stepLists.Clear();
        transcript.Clear();
        said.Clear();
        asking.Clear();
        turn = null;
        Working(null);
        Page();
    }

    ////////// 13. MENUS //////////

    // A row in a menu: a title, and a quieter line under it when there is one.
    Border Row(string text, string? under, Action click)
    {
        var stack = new StackPanel { Children = { new TextBlock { Text = text, FontSize = 13, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis } } };
        if (under is not null) stack.Children.Add(new TextBlock { Text = under, FontSize = 11, Foreground = Ink4 });
        var row = new Border { Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = stack };
        Hover(row, Page_, Brushes.Transparent);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    // A small menu under a button, closed by clicking anywhere else. fill puts its rows in, and gets how to close it.
    void Popup(FrameworkElement anchor, Func<StackPanel, Action, Task> fill)
    {
        var rows = new StackPanel();
        var menu = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4,
            Child = new Border
            {
                Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6),
                MinWidth = 220, MaxWidth = 320, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.16 },
                Resources = { MergedDictionaries = { Thin } },
                Child = new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows },
            },
        };
        menu.IsOpen = true;
        _ = fill(rows, () => menu.IsOpen = false);
    }

    // 13. The header's menu: Copy chat while there is one, Settings, and signing out.
    Task Menu(StackPanel rows, Action close)
    {
        if (Talking) rows.Children.Add(Row("Copy chat", "The whole chat, every step, as text", () =>
        {
            close();
            Clipboard.SetText(transcript.Length > 0 ? transcript.ToString() : "(nothing said yet)");
            Tell("Copied the chat.");
        }));
        rows.Children.Add(Row("Settings", "What he may do, your lingos' settings, and more", () => { close(); SettingsWindow.Open(settings, this); }));
        rows.Children.Add(new Border { Height = 1, Background = Rule, Margin = new Thickness(6, 4, 6, 4) });
        rows.Children.Add(Row("Sign out", settings.User, () => { close(); SignOut(); }));
        return Task.CompletedTask;
    }

    // What his programs may do here. The core enforces it, and a change restarts it with the new list.
    Task Permissions(StackPanel rows, Action close)
    {
        rows.Children.Add(Note("What Onsay may do on this computer:"));
        foreach (var (name, words, _) in Core.Permissions)
        {
            var check = new CheckBox { Content = words, IsChecked = Core.Allowed(settings).Contains(name), FontSize = 13, Foreground = Ink, Margin = new Thickness(10, 5, 10, 5) };
            check.Checked += (_, _) => Permit(name, true);
            check.Unchecked += (_, _) => Permit(name, false);
            rows.Children.Add(check);
        }
        return Task.CompletedTask;
    }

    void Permit(string name, bool on)
    {
        var allow = Core.Allowed(settings);
        allow.Remove(name);
        if (on) allow.Add(name);
        settings.Allow = allow;
        settings.Save();
        Labels();
        Core.Restart();
    }

    // The permissions changed somewhere else, in Settings.
    public void Permitted() => Labels();

    // The words on the permissions button.
    void Labels() => ((TextBlock)permits.Child).Text = $"Permissions · {Core.Allowed(settings).Count} of {Core.Permissions.Length}";

    // A thin rounded scrollbar with no arrows. Horizontal ones never appear in the chat, so they are hidden.
    static readonly ResourceDictionary Thin = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
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
                            <Border x:Name="bar" Background="#D8D9E5" CornerRadius="3" Margin="3,2" />
                            <ControlTemplate.Triggers>
                              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bar" Property="Background" Value="#A9ACBC" /></Trigger>
                              <Trigger Property="IsDragging" Value="True"><Setter TargetName="bar" Property="Background" Value="#7B7F92" /></Trigger>
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
