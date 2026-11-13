// ChatWindow.cs - the card that opens beside him, or docked along a side of his screen: signing in, your to-dos, or one
// to-do open, and the look every window shares. It only draws: each to-do is kept by the app (ToDos.cs) and run by the agent lingo on the floor,
// every prompt going to the app's /onsay (Host.cs), and what comes back and what the lingos ask is shown here. The list is a to-do list that does what you say: the box at the bottom adds one, each row says how far
// it has got, and Not quite on a done one makes a new one that follows it up. Opening one shows its whole conversation,
// and the box there replies to it. Its X closes it, and so do he (his back has an X while it is open) and Esc. While he
// works his steps show as they happen, folded under one line once he answers. It sits beside him and moves with him, or docked to the left,
// right or bottom of his screen, which keeps that strip so windows make room, the way a browser's developer tools dock.
// Its top bar dragged carries him and the card, and dropped on a side of the screen docks it there.
//
// 1.  Open(body, monitor, focus)          opens, or follows him, next to his body rect in pixels, growing out of him, or
//                                         in its strip when docked
//     Spot(body, monitor)                 where it goes beside him, for him to move both in one go
//     Pin(), Strip(edge, monitor, scale)  docked, the strip of the screen it keeps, asked of Windows
//     Sit(edge), Dress()                  where it sits, null beside him, and its look for that
//     Resize()                            the docked card's inner edge dragged
//     Grab, Carry, LetGo, Preview         its top bar dragged, carrying him, and the strip it would dock to
// 2.  Dismiss()                           puts the card away, shrinking back into him, and finishes anything half said
// 3.  Stop()                              Stop, or Alt+Shift+O from any thread: the lingo client starts again, which
//                                         ends whatever it was doing
// 4.  SignInBox(done, centre), SignOut()  signing in through the browser, the same box here, in setting him up and in
//                                         Settings, Account, and signing out from there
// 5.  Refresh(), Page()                   the onsay lingo's status, and the page that fits it: sign in, the list,
//                                         one to-do open, or Screens, every screen open
// 6.  Send(), Add(text), Ask(text)        the box: a new to-do from the list, or a reply to the one open, followed as it
//                                         runs, and his answer or his questions
//     List(), Rows(), Item(item)          every to-do, asked every second, each a row of one height with the screens it
//                                         opened in a row along its bottom, and
//                                         Fix(item), Not quite on one. Named(id) is one by what was asked
//     Load(id), Follow(...)               one to-do open, its whole conversation, and what it does as it runs
//     ReadKey(), To(method, path, body)   the prompt key, and a request to the app's /onsay with it
//     Replied(...)                        his answer, with how it ended and how long it took
//     Trial()                             once Onsay's AI trial is over, a card offering the subscription or your own key
// 7.  Working(text)                       the bar: what he is doing right now and for how long, null hides it
// 8.  Tell(text)                          a line across the top, like "Copied the chat."
// 9.  Say(text), Yours(text), His()       his line spoken by the face in the header, drawn as Markdown, and the rows
//                                         of the conversation
// 10. Questions(list, send)               his questions back, each picked or typed, answered together with one Send
//     Poll(), Asked(waiting)              a lingo's questions (chat.ask), asked for every second, each a card here
//     Lingo(kind, text)                   a lingo's own: what it says (chat.say) and a step it shows (chat.step)
//     Steps(), AddStep(...), Fold(...)    a message's steps as it runs, each a line, nested ones indented, then folded
// 11. Clear()                             back to the list, nothing open
// 12. Menu(), SitMenu()                   the header's menu: Copy chat, from the session, and Apps and settings, and
//                                         where the chat sits, as four small pictures of a screen. Pop is a menu, Row its row
// 13. The look every window shares        Brand, the colour tokens and the fonts, Hex, and the pieces: Lifted, Button,
//                                         Paragraph, Label, Note, Toggle, Switch, Selectable, Right, ScaleBoth, Shown,
//                                         Hover, the Menus of a right click and the Thin scrollbar
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
using static Onsay.Desktop.Native;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Onsay.Desktop;

sealed class ChatWindow : Window
{
    ////////// SIZES AND ICONS //////////

    public const double Shadow = 8;      // transparent room around the card for its offset shadow, in DIPs
    const double Wide = 420;             // the card's size beside him, in DIPs
    const double Tall = 600;
    const double Near = 28;              // how close to a side of the screen its top bar is dropped to dock there, in DIPs
    const double Column = 760;           // the widest the words get, docked along the bottom, so lines stay easy to read
    // Glyphs from Icons.
    const string BackIcon = "";
    const string CloseIcon = "";
    const string DoneIcon = "";
    const string WorkingIcon = "";
    const string AskingIcon = "";
    const string WaitingIcon = "";
    const string RedoIcon = "";
    const string MoreIcon = "";
    const string SendIcon = "";
    const string SitIcon = "";
    const string OpenIcon = "";
    const string FoldedIcon = "";
    const string ShowIcon = "";   // an eye

    ////////// STATE //////////

    readonly Settings settings;
    readonly Action<bool> setOpen;
    readonly BuddyWindow him;                    // his window, carried along while the card's top bar is dragged
    readonly OnsayView face = new() { Width = 28, Height = 28 };
    readonly TextBlock subtitle = new() { FontSize = 11.5, Foreground = Ink3, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border back;   // the header's way back to the list, while a to-do is open
    readonly Border screensButton;

    // The pages. Only one shows at a time, see Page.
    readonly FrameworkElement signInPage;
    readonly ScrollViewer listPage = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel listRows = new();
    readonly StackPanel asksPanel = new();       // a lingo's own questions, while the list shows
    readonly Border screensBar = new() { Margin = new Thickness(14, 8, 14, 0), MaxWidth = Column };   // the open to-do's screens, over the box
    readonly FrameworkElement empty;             // the greeting, while there are no to-dos yet
    readonly ScrollViewer chatPage = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ScrollViewer screensPage = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };   // every screen open, a card each
    readonly StackPanel lines = new() { Margin = new Thickness(18, 16, 18, 6), MaxWidth = Column };
    readonly TextBlock ready = new() { FontSize = 12.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap };
    readonly Ellipse readyDot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 8, 0) };

    // The banner across the top, the bar while he works, and the box.
    readonly Border banner = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(16, 8, 16, 8), Background = Butter };
    readonly TextBlock bannerText = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Ink };
    readonly DockPanel working = new() { Margin = new Thickness(16, 8, 12, 0), Visibility = Visibility.Collapsed, MaxWidth = Column };
    readonly TextBlock workingText = new() { FontSize = 12.5, Foreground = Ink2, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock workingTime = new() { FontSize = 12, FontFamily = Mono, Foreground = Ink3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
    readonly Ellipse[] dots = Enumerable.Range(0, 3).Select(_ => new Ellipse { Width = 6, Height = 6, Fill = Accent, Margin = new Thickness(0, 0, 4, 0), Opacity = 0.3 }).ToArray();
    readonly FrameworkElement input;
    readonly Border fixingChip;                  // over the box while the next to-do follows one up
    readonly TextBlock fixingText = new() { FontSize = 12, Foreground = Ink2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border send;
    readonly TextBlock sendGlyph = new() { Text = SendIcon, FontFamily = Icons, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Border showWork;                    // Use desktop: on the list for the next to-do, in one for that one
    readonly TextBlock showWorkGlyph = new() { Text = ShowIcon, FontFamily = Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock showWorkText = new() { Text = "Use desktop", FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox box = new()
    {
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        FontSize = 14,
        Foreground = Ink,
        CaretBrush = Accent,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 24,
        MaxHeight = 140,
        Padding = new Thickness(0),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    readonly TextBlock hint = new() { FontSize = 14, Foreground = Ink4, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 0) };

    // The onsay lingo's status: {signed_in, signing_in, failed, running, connected, handle, device, key}. Null until it
    // first answers.
    JsonObject? status;
    bool refreshing;
    string? session;                             // the to-do open, its session in the onsay lingo, or null for the list
    bool showingScreens;                         // the Screens page shows, over the list or the to-do open
    string? promptKey;                           // the onsay lingo's prompt key, read again for each message
    int chat;                                    // which page this is, so an answer to one left behind is dropped
    bool waiting;                                // a reply to the one open is out
    JsonArray items = new();                     // every to-do as /onsay/sessions last gave them, the newest first
    string? listed;                              // and as text, so the list is drawn again only when it changed
    string? newest;                              // the newest to-do drawn, so the list goes back to the top for a new one
    bool listing;
    string? loaded;                              // the open one's status when it was last loaded
    int watched = -1;                            // the page whose to-do is followed as it runs, see Load
    string? fixing;                              // the to-do Not quite was pressed on, which the next one follows up
    bool stopping;                               // Stop was pressed while it was
    bool greeted;
    bool following = true;                       // the chat keeps to its bottom as new lines come
    DateTime doingSince;                         // when what he is doing now started, for the bar's timer
    (RichTextBox row, string text)? speaking;
    CancellationTokenSource? signing;            // a sign in waiting on the browser
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    // Each message's steps: its block in the chat, the line it folds under, its lines, how many and how long it took.
    sealed class StepList
    {
        public required Border Root;
        public required TextBlock Head;
        public required StackPanel Body;
        public int Count;
        public int Heard;                        // of the steps the onsay lingo kept, how many are lines here
        public double Ms;
    }
    readonly List<StepList> stepLists = new();
    // The cards of the questions a lingo waits on, by their id in the lingo client (chat.ask).
    readonly Dictionary<double, Border> lingoCards = new();
    // A prompt is waited for as long as it works: each of its calls ends within 10 minutes in the onsay lingo, its steps
    // show meanwhile, and Stop ends it.
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    // Where Onsay's AI is subscribed to, on Onsay's website, and whether that was offered since the app started.
    const string SubscribePage = "https://onsay.dev/subscribe";
    bool offeredSubscription;

    public bool Busy => waiting;                    // a message sent from here is being answered

    /* Whether nothing is happening that a restart would cut short, so an update may go in: the card put away, which a
       lingo's question opens, and no to-do working, as the onsay lingo lists them. Not knowing counts as busy. */
    public async Task<bool> Idle()
    {
        if (Showing || waiting) return false;
        try
        {
            await ReadKey();
            using var res = await Http.SendAsync(To(HttpMethod.Get, "/sessions"));
            if (JsonNode.Parse(await res.Content.ReadAsStringAsync()) is not JsonArray all) return false;
            return all.All(i => $"{i?["status"]}" != "working");
        }
        catch (Exception) { return false; }
    }
    bool Showing => IsVisible && !closing;          // open, and not on its way out
    internal bool Carrying => carrying;             // its top bar is being dragged, carrying him
    bool SignedIn => (bool?)status?["signed_in"] == true;

    // Where the card sits, and how it gets there.
    readonly Grid whole;                            // the card and its shadow, which open and close as one
    readonly Border card;
    readonly Border clip;
    readonly Border drop;                           // the offset shadow under the card
    readonly Border grip;                           // the docked card's inner edge, dragged to make it wider or taller
    readonly ScaleTransform zoom = new(1, 1);
    readonly IntPtr hwnd;
    Side? side;                                     // where it sits beside him while open, kept so it never flips
    public Side? SideNow => Showing && Docked is null ? side : null;   // so a card beside him keeps clear of it
    public Edge? Docked => settings.Docked;         // the side of his screen it is docked along, or null beside him
    double DockSize => settings.DockSize ?? (Docked == Edge.Bottom ? 320 : Wide);
    RECT? pinned;                                   // the strip last asked of Windows, so asking the same again is skipped
    bool closing;                                   // fading out, and hidden once it has
    // Its top bar dragged: where the pointer and he were when it started, and the side of the screen it would dock to.
    bool carrying;
    bool carried;
    POINT grabbed;
    POINT start;
    RECT home;
    Edge? landing;
    Window? preview;                                // the strip it would dock to, shown while dragged near a side

    public ChatWindow(Settings settings, Action<bool> setOpen, BuddyWindow him)
    {
        this.settings = settings;
        this.setOpen = setOpen;
        this.him = him;
        Native.Bare(this);
        Width = Wide + Shadow * 2;
        Height = Tall + Shadow * 2;
        FontFamily = Ui;

        // The header: him and who he is, every screen open, where the card sits, the menu and close. Dragged, it carries
        // him and the card together, and dropped on a side of the screen, it docks there.
        back = Button(BackIcon, Kind.Icon, () =>
        {
            if (showingScreens) showingScreens = false;
            else Clear();
            Page();
            box.Focus();
        }, "Back");
        back.Margin = new Thickness(0, 0, 4, 0);
        screensButton = Button("Screens", Kind.Ghost, () =>
        {
            showingScreens = true;
            Page();
        }, "Every screen open, what your apps are showing as they work");
        screensButton.Margin = new Thickness(0, 0, 2, 0);
        Border sit = null!;
        sit = Button(SitIcon, Kind.Icon, () => SitMenu(sit), "Where the chat sits");
        Border more = null!;
        more = Button(MoreIcon, Kind.Icon, () => Menu(more), "More");
        var close = Button(CloseIcon, Kind.Icon, () => setOpen(false), "Close (Esc)");
        var header = new DockPanel { Margin = new Thickness(12, 10, 8, 10), Background = Brushes.Transparent };
        header.Children.Add(Right(close));
        header.Children.Add(Right(more));
        header.Children.Add(Right(sit));
        header.Children.Add(Right(screensButton));
        header.Children.Add(back);
        header.Children.Add(face);
        header.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Onsay", FontFamily = Display, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ink }, subtitle },
        });
        header.MouseLeftButtonDown += (_, _) => Grab(header);
        header.MouseMove += (_, _) => Carry();
        header.MouseLeftButtonUp += (_, _) => LetGo(header);
        header.LostMouseCapture += (_, _) => LetGo(header);
        banner.Child = bannerText;

        // The sign in page.
        signInPage = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 20),
            Children =
            {
                new OnsayView { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) },
                Paragraph("Sign in so I can help on this computer.", centre: true),
                SignInBox(centre: true),
            },
        };

        // The list: a lingo's own questions, then every to-do, the newest at the top. With none yet, a greeting.
        empty = new StackPanel
        {
            Margin = new Thickness(10, 18, 10, 12),
            Children =
            {
                new TextBlock { Text = "What can I do for you?", FontFamily = Display, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Ink },
                new DockPanel { Margin = new Thickness(0, 8, 0, 4), Children = { readyDot, ready } },
            },
        };
        readyDot.VerticalAlignment = VerticalAlignment.Top;
        readyDot.Margin = new Thickness(0, 6, 8, 0);
        listPage.Content = new StackPanel { Margin = new Thickness(8, 4, 8, 6), MaxWidth = Column, Children = { empty, asksPanel, listRows } };

        // The Screens page: every screen open, a card each, a click showing it full screen.
        screensPage.Content = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 6),
            Children =
            {
                new TextBlock { Text = "Screens", FontFamily = Display, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(8, 0, 0, 4) },
                ScreensWindow.Every(Named),
            },
        };

        // The chat page follows new content only while you are at the bottom, so scrolling up to read stays put.
        chatPage.Content = lines;
        chatPage.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) following = chatPage.VerticalOffset >= chatPage.ScrollableHeight - 24;
            else if (following) chatPage.ScrollToEnd();
        };

        // The bar while he works: the dots, what he is doing, for how long, and Stop with the key that does it anywhere.
        working.Children.Add(Right(Button("Stop", Kind.Danger, Stop, "Stop him now. Alt+Shift+O does the same from any app")));
        working.Children.Add(Right(new Border
        {
            Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Stops him from any app, at any time",
            Child = new TextBlock { Text = "Alt+Shift+O", FontSize = 10.5, FontFamily = Mono, Foreground = Ink3 },
        }));
        working.Children.Add(Right(workingTime));
        var doing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var d in dots) doing.Children.Add(d);
        doing.Children.Add(workingText);
        working.Children.Add(doing);
        tick.Tick += (_, _) => workingTime.Text = Took((DateTime.Now - doingSince).TotalMilliseconds, live: true);

        /* The box: what you type on top, and under it Send. Enter sends, Shift+Enter starts a new line,
           and it grows to a few lines before it scrolls. A click anywhere in it puts the cursor in the text. */
        send = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand, ToolTip = "Send (Enter)",
            HorizontalAlignment = HorizontalAlignment.Right, Child = sendGlyph,
        };
        send.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Send();
        };
        showWork = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 0, 10, 0), Height = 30, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { showWorkGlyph, showWorkText } },
        };
        showWork.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowWork(!Watched());
        };
        var field = new Border
        {
            BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Margin = new Thickness(12, 8, 12, 12), Padding = new Thickness(12, 10, 8, 8), Background = Card, Cursor = Cursors.IBeam,
            Child = new StackPanel
            {
                Children =
                {
                    new Grid { Children = { hint, box }, Margin = new Thickness(0, 0, 4, 6) },
                    new DockPanel { Cursor = Cursors.Arrow, Children = { Right(send), showWork } },
                },
            },
        };
        field.MouseLeftButtonUp += (_, _) => box.Focus();
        fixingChip = new Border
        {
            Margin = new Thickness(12, 8, 12, 0), Padding = new Thickness(10, 0, 0, 0), CornerRadius = new CornerRadius(7),
            Background = BlueSoft, Visibility = Visibility.Collapsed,
            Child = new DockPanel { Children = { Right(Button(CloseIcon, Kind.Icon, () => Fix(null), "Make it a new to-do instead")), fixingText } },
        };
        input = new StackPanel { MaxWidth = Column, Children = { fixingChip, field } };
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

        // Everything stacked: the pages share the one row that grows.
        var pages = new Grid { Children = { signInPage, listPage, chatPage, screensPage } };
        var layout = new Grid();
        UIElement[] rows = { header, new Border { Height = 1, Background = Rule }, banner, pages, working, screensBar, input };
        for (int i = 0; i < rows.Length; i++)
        {
            layout.RowDefinitions.Add(new RowDefinition { Height = rows[i] == pages ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetRow(rows[i], i);
            layout.Children.Add(rows[i]);
        }

        /* The card on its offset shadow, the brand's lifted look, which costs nothing to draw however much moves in it. The
           two grow and fade as one when it opens and closes, see Appear. Docked, it is a flat strip with a line on its
           inner edge, which is dragged to resize it. */
        clip = new Border { ClipToBounds = true, Child = layout };
        card = new Border { Background = Card, BorderBrush = Edge_, Child = clip };
        drop = new Border { Background = DropSoft, CornerRadius = new CornerRadius(10), Margin = new Thickness(Shadow / 2, Shadow / 2, 0, 0) };
        grip = new Border { Background = Brushes.Transparent };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            grip.CaptureMouse();
        };
        grip.MouseMove += (_, _) =>
        {
            if (grip.IsMouseCaptured) Resize();
        };
        grip.MouseLeftButtonUp += (_, _) => grip.ReleaseMouseCapture();
        grip.LostMouseCapture += (_, _) =>
        {
            settings.Save();
            Pin();
        };
        whole = new Grid { Children = { drop, card, grip }, RenderTransform = zoom };
        Content = whole;
        Dress();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            setOpen(false);
        };
        // Alt+F4 puts the card away rather than destroying it. The app closing still closes it, and gives back its strip.
        Closing += (_, e) =>
        {
            if (!IsVisible || Application.Current.MainWindow?.IsVisible != true) return;
            e.Cancel = true;
            setOpen(false);
        };
        Closed += (_, _) => Native.Unreserve(hwnd);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Native.Unreserve(hwnd);
        Resources.MergedDictionaries.Add(Thin);
        hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Something else moved the strips about, like the taskbar or a screen: its strip is worked out again.
        HwndSource.FromHwnd(hwnd).AddHook((IntPtr window, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            bool moved = (msg == (int)Native.StripMoved && (int)wParam == 1) || msg == Native.WM_DISPLAYCHANGE;
            if (moved && Showing) Dispatcher.BeginInvoke(Pin);
            return IntPtr.Zero;
        });
        Page();
        Core.Start(settings);
        // Whether this computer is signed in and connected, asked again every few seconds while the card shows.
        var look = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        look.Tick += (_, _) =>
        {
            if (Showing || status is null) Refresh();
        };
        look.Start();
        // Every to-do, asked again every second while the card shows.
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        poll.Tick += (_, _) =>
        {
            if (Showing && SignedIn) List();
        };
        poll.Start();
    }

    // The box's hint, and Send lit up only while something is typed and, with a to-do open, no reply to it is out.
    void ReadyInput()
    {
        hint.Text = "Add a to-do";
        if (fixing is not null) hint.Text = "What wasn't right?";
        if (session is not null) hint.Text = "Reply";
        hint.Visibility = Shown(box.Text.Length == 0);
        bool any = box.Text.Trim().Length > 0 && !(session is not null && Busy);
        send.Background = any ? Accent : Page_;
        sendGlyph.Foreground = any ? Brushes.White : Ink4;
        send.IsHitTestVisible = any;
        bool watched = Watched();
        showWork.Background = watched ? BlueSoft : Brushes.Transparent;
        showWorkGlyph.Foreground = watched ? Accent : Ink3;
        showWorkText.Foreground = watched ? Accent : Ink3;
        if (session is null)
            showWork.ToolTip = watched ? "Your next to-do shows its work: he works in front of you, his hand following. Click to have him work unseen" : "Your next to-do is done unseen. Click to watch him work";
        else
            showWork.ToolTip = watched ? "You're watching this to-do's work. Click to have him finish it unseen" : "This to-do is done unseen. Click to watch him work on it";
    }

    // Use desktop, as the box shows it now: for the to-do open, else for the next one made from the list.
    bool Watched()
    {
        if (session is null) return settings.ShowWork;
        return (bool?)items.FirstOrDefault(i => $"{i?["session"]}" == session)?["show"] == true;
    }

    // On the list it is kept for each new to-do. In one, that to-do changes from its next step.
    async void ShowWork(bool on)
    {
        if (session is null)
        {
            settings.ShowWork = on;
            settings.Save();
            ReadyInput();
            return;
        }
        if (items.FirstOrDefault(i => $"{i?["session"]}" == session) is JsonObject item) item["show"] = on;
        ReadyInput();
        try
        {
            await ReadKey();
            using var res = await Http.SendAsync(To(HttpMethod.Post, $"/sessions/{session}/show", new JsonObject { ["show"] = on }));
        }
        catch (Exception) { }
    }

    ////////// 1-3. OPEN, CLOSE, WHERE IT SITS, STOP //////////

    /* 1. focus false shows it without taking the keyboard, as when he comes back from using the computer. It is put where it
          belongs before it shows, so it never draws a frame where it last was, and then grows out of him. Docked, it takes
          its strip of the screen as it shows. */
    public void Open(RECT body, MONITORINFO monitor, bool focus)
    {
        bool appearing = !Showing;
        if (Docked is null)
        {
            var (_, p) = Spot(body, monitor);
            double scale = Native.ScaleAt(monitor.rcMonitor.Center);
            if (appearing)
            {
                Left = p.X / scale;
                Top = p.Y / scale;
            }
            Native.Place(hwnd, p);
        }
        if (appearing)
        {
            ShowActivated = focus;
            Show();
            ShowActivated = true;
            Appear();
            Refresh();
        }
        Pin();
        if (!focus) return;
        Activate();
        box.Focus();
        if (greeted) return;
        greeted = true;
        // He greets you once he has arrived in the header, so the two movements do not cancel out.
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(260);
            face.React("greet");
        });
    }

    // 1. Where it goes beside him, without moving it, so he can move both in one go. Its shadow room is most of the gap.
    // While open it keeps to its side for as long as that fits. Sized by the scaling of the monitor it lands on, never
    // his, which may still be another monitor's as he crosses between two.
    public (IntPtr hwnd, POINT p) Spot(RECT body, MONITORINFO monitor)
    {
        double scale = Native.ScaleAt(monitor.rcMonitor.Center);
        int width = (int)Math.Round(Width * scale);
        int height = (int)Math.Round(Height * scale);
        int gap = (int)Math.Round((8 - Shadow) * scale);
        var p = Dock.Beside(monitor, body, width, height, gap, Showing ? side : null, out var s);
        side = s;
        return (hwnd, p);
    }

    /* 1. Docked, its strip: the whole height of the side of his screen, or its whole width at the bottom, kept by Windows
          so other windows make room, on the screen he is on. */
    void Pin()
    {
        if (Docked is not { } edge) return;
        var spot = him.BodyRect().Center;
        var strip = Strip(edge, Native.MonitorAt(spot, Native.MONITOR_DEFAULTTONEAREST), Native.ScaleAt(spot));
        if (pinned is not { } last || !last.Equals(strip) || Native.Reserved is null)
        {
            pinned = strip;
            strip = Native.Reserve(hwnd, edge, strip);
        }
        else strip = Native.Reserved.Value;
        Native.Place(hwnd, new POINT(strip.Left, strip.Top), (strip.Width, strip.Height));
    }

    // 1. The strip along a side of the monitor's work area, DockSize wide, or tall at the bottom, but never more than half.
    RECT Strip(Edge edge, MONITORINFO monitor, double scale)
    {
        var work = monitor.rcWork;
        int across = edge == Edge.Bottom ? work.Height : work.Width;
        int size = Math.Min((int)Math.Round(DockSize * scale), across / 2);
        return edge switch
        {
            Edge.Left => new RECT { Left = work.Left, Top = work.Top, Right = work.Left + size, Bottom = work.Bottom },
            Edge.Right => new RECT { Left = work.Right - size, Top = work.Top, Right = work.Right, Bottom = work.Bottom },
            _ => new RECT { Left = work.Left, Top = work.Bottom - size, Right = work.Right, Bottom = work.Bottom },
        };
    }

    /* 1. Where the card sits: null beside him, moving with him, or docked along a side of his screen. Kept for next time,
          and the card moves there at once if it shows. */
    void Sit(Edge? edge)
    {
        if (edge == Docked) return;
        settings.Docked = edge;
        settings.Save();
        Dress();
        if (!Showing) return;
        if (edge is not null)
        {
            Pin();
            return;
        }
        Native.Unreserve(hwnd);
        pinned = null;
        side = null;
        Width = Wide + Shadow * 2;
        Height = Tall + Shadow * 2;
        var body = him.BodyRect();
        var (_, p) = Spot(body, Native.MonitorAt(body.Center, Native.MONITOR_DEFAULTTONEAREST));
        Native.Place(hwnd, p);
    }

    // 1. Its look for where it sits: lifted and rounded beside him, flat and square with its inner edge to drag docked.
    void Dress()
    {
        var edge = Docked;
        bool docked = edge is not null;
        whole.Margin = docked ? new Thickness(0) : new Thickness(Shadow, Shadow, Shadow / 2, Shadow / 2);
        card.Margin = docked ? new Thickness(0) : new Thickness(0, 0, Shadow / 2, Shadow / 2);
        drop.Visibility = Shown(!docked);
        card.CornerRadius = new CornerRadius(docked ? 0 : 10);
        clip.CornerRadius = new CornerRadius(docked ? 0 : 9);
        card.BorderThickness = edge switch
        {
            Edge.Left => new Thickness(0, 0, 1, 0),
            Edge.Right => new Thickness(1, 0, 0, 0),
            Edge.Bottom => new Thickness(0, 1, 0, 0),
            _ => new Thickness(1),
        };
        grip.Visibility = Shown(docked);
        grip.Width = edge == Edge.Bottom ? double.NaN : 6;
        grip.Height = edge == Edge.Bottom ? 6 : double.NaN;
        grip.HorizontalAlignment = edge switch
        {
            Edge.Left => HorizontalAlignment.Right,
            Edge.Right => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Stretch,
        };
        grip.VerticalAlignment = edge == Edge.Bottom ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        grip.Cursor = edge == Edge.Bottom ? Cursors.SizeNS : Cursors.SizeWE;
    }

    // 1. The docked card's inner edge follows the pointer, between a narrow chat and half the screen. Windows is asked
    // for the new strip once it is let go.
    void Resize()
    {
        if (Docked is not { } edge || Native.Reserved is not { } now) return;
        Native.GetCursorPos(out var c);
        var work = Native.MonitorAt(now.Center, Native.MONITOR_DEFAULTTONEAREST).rcWork;
        double scale = Native.ScaleAt(now.Center);
        int px = edge switch
        {
            Edge.Left => c.X - now.Left,
            Edge.Right => now.Right - c.X,
            _ => now.Bottom - c.Y,
        };
        int most = (edge == Edge.Bottom ? work.Height : work.Width) / 2;
        int least = (int)Math.Round((edge == Edge.Bottom ? 200 : 320) * scale);
        px = Compat.Clamp(px, least, Math.Max(least, most));
        settings.DockSize = px / scale;
        var r = now;
        if (edge == Edge.Left) r.Right = r.Left + px;
        if (edge == Edge.Right) r.Left = r.Right - px;
        if (edge == Edge.Bottom) r.Top = r.Bottom - px;
        Native.Place(hwnd, new POINT(r.Left, r.Top), (r.Width, r.Height));
    }

    /* 1. The top bar pressed: dragged, it carries him and the card together, the card on his side as always. Docked, the
          card comes off its side first and he comes to the pointer with it. Near a side of the screen the strip it would
          take shows, and let go there it docks and he walks back to where he was. A click is only a click. */
    void Grab(FrameworkElement bar)
    {
        carrying = true;
        carried = false;
        landing = null;
        Native.GetCursorPos(out grabbed);
        home = him.Frame();
        start = new POINT(home.Left, home.Top);
        bar.CaptureMouse();
    }

    void Carry()
    {
        if (!carrying) return;
        Native.GetCursorPos(out var c);
        double scale = Native.ScaleAt(c);
        if (!carried && Math.Abs(c.X - grabbed.X) + Math.Abs(c.Y - grabbed.Y) < 6 * scale) return;
        if (!carried)
        {
            carried = true;
            if (Docked is not null)
            {
                Sit(null);
                start = new POINT(c.X - home.Width / 2, c.Y - home.Height / 2);
                grabbed = c;
            }
        }
        // His body stays on the screen, his window's room around it may hang off.
        var monitor = Native.MonitorAt(c, Native.MONITOR_DEFAULTTONEAREST);
        var work = monitor.rcWork;
        int room = (home.Width - him.BodyRect().Width) / 2;
        int x = Compat.Clamp(start.X + c.X - grabbed.X, work.Left - room, work.Right - home.Width + room);
        int y = Compat.Clamp(start.Y + c.Y - grabbed.Y, work.Top - room, work.Bottom - home.Height + room);
        him.Put(new POINT(x, y));
        int near = (int)Math.Round(Near * scale);
        landing = null;
        if (c.X <= work.Left + near) landing = Edge.Left;
        else if (c.X >= work.Right - near) landing = Edge.Right;
        else if (c.Y >= work.Bottom - near) landing = Edge.Bottom;
        Preview(landing is { } edge ? Strip(edge, monitor, scale) : null);
    }

    void LetGo(FrameworkElement bar)
    {
        if (!carrying) return;
        carrying = false;
        if (bar.IsMouseCaptured) bar.ReleaseMouseCapture();
        Preview(null);
        if (!carried) return;
        if (landing is not { } edge)
        {
            him.Park(dropped: true);
            return;
        }
        Sit(edge);
        him.Park(dropped: false, slide: true);
    }

    // 1. A see-through strip in the brand colour where the card would dock, or null to hide it. Never clicked.
    void Preview(RECT? strip)
    {
        if (strip is not { } r)
        {
            preview?.Hide();
            return;
        }
        if (preview is null)
        {
            preview = new Window();
            Native.Bare(preview);
            preview.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x26, Accent.Color.R, Accent.Color.G, Accent.Color.B)),
                BorderBrush = Accent,
                BorderThickness = new Thickness(2),
            };
            Native.ClickThrough(preview);
        }
        preview.Show();
        Native.Place(new WindowInteropHelper(preview).Handle, new POINT(r.Left, r.Top), (r.Width, r.Height));
        Native.Raise(new WindowInteropHelper(preview).Handle);
    }

    // 2. It shrinks back into him as it fades, then hides. Docked, it only fades, and gives its strip back once gone.
    public void Dismiss()
    {
        face.Hush();
        if (speaking is { } s) s.row.Document = Markdown.Render(s.text);
        speaking = null;
        if (!Showing) return;
        closing = true;
        Animate(0, Docked is null ? 0.96 : 1, 130, EasingMode.EaseIn, () =>
        {
            if (!closing) return;
            closing = false;
            Hide();
            Native.Unreserve(hwnd);
            pinned = null;
        });
    }

    // Out of the side of him it sits on: from a little smaller and clear to whole, quick and easing out.
    void Appear()
    {
        bool wasClosing = closing;   // closing, it turns round from wherever it got to
        closing = false;
        whole.RenderTransformOrigin = side switch
        {
            Side.Above => new Point(0.5, 1),
            Side.Below => new Point(0.5, 0),
            Side.Left => new Point(1, 0.5),
            _ => new Point(0, 0.5),
        };
        if (!wasClosing)
        {
            whole.BeginAnimation(OpacityProperty, null);
            ScaleBoth(zoom, null);
            whole.Opacity = 0;
            zoom.ScaleX = Docked is null ? 0.94 : 1;
            zoom.ScaleY = zoom.ScaleX;
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
        ScaleBoth(zoom, new DoubleAnimation(size, time) { EasingFunction = ease });
    }


    // 3. Whatever the lingo client is running ends as it starts again, the message waiting here with it.
    public void Stop()
    {
        stopping = true;
        Core.Start(settings);
        Dispatcher.BeginInvoke(() =>
        {
            if (Busy) Working("Stopping");
        });
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
        var codeShown = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = align,
            Children =
            {
                new TextBlock { Text = "Your code", FontSize = 12, Foreground = Ink3, HorizontalAlignment = align },
                new Border
                {
                    Background = Page_,
                    BorderBrush = Edge_,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 6, 18, 8),
                    Margin = new Thickness(0, 6, 0, 10),
                    HorizontalAlignment = align,
                    Child = code,
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
            error.Visibility = Visibility.Collapsed;
            start.Visibility = Visibility.Collapsed;
            try
            {
                var (got, why) = await Task.Run(() => Account.Onsay("sign_in"));
                if (got is null) throw new Exception(why);
                if (got["url"] is { } url)
                {
                    code.Text = $"{got["code"]}";
                    codeShown.Visibility = Visibility.Visible;
                    Native.Open($"{url}");
                    JsonNode? now = null;
                    while ((bool?)now?["signed_in"] != true)
                    {
                        await Task.Delay(2000, stop);
                        (now, _) = await Task.Run(() => Account.Onsay("status"));
                        if (now is null || (bool?)now["signed_in"] == true || (bool?)now["signing_in"] == true) continue;
                        throw new Exception(now["failed"] is { } failed ? $"{failed}." : "Signing in didn't finish. Try again.");
                    }
                }
                Refresh();
                face.React("greet");
                done?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                error.Text = e.Message;
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                signing = null;
                codeShown.Visibility = Visibility.Collapsed;
                start.Visibility = Visibility.Visible;
            }
        });
        start.HorizontalAlignment = align;
        return new StackPanel { Children = { start, codeShown, error } };
    }

    // 4. Why it couldn't, or null. The lingo client runs on, for this computer alone.
    public string? SignOut()
    {
        var why = Account.Onsay("sign_out").why;
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
        var (answer, _) = await Task.Run(() => Account.Onsay("status"));
        if (answer is JsonObject now) status = now;
        refreshing = false;
        Page();
    }

    // 5. The list until a to-do is opened, and while the onsay lingo hasn't answered yet, getting ready.
    void Page()
    {
        bool known = status is not null;
        bool on = SignedIn;
        bool open = on && session is not null;
        bool connected = (bool?)status?["connected"] == true;
        signInPage.Visibility = Shown(known && !on);
        listPage.Visibility = Shown(!known || (on && !open && !showingScreens));
        chatPage.Visibility = Shown(open && !showingScreens);
        screensPage.Visibility = Shown(on && showingScreens);
        back.Visibility = Shown(open || (on && showingScreens));
        input.Visibility = Shown(on);
        screensButton.Visibility = Shown(on && !showingScreens);

        if (!known) subtitle.Text = "";
        else if (on) subtitle.Text = $"{status!["handle"]}";
        else subtitle.Text = "Not signed in";

        if (known && !on) Working(null);
        ReadyInput();

        readyDot.Fill = connected ? Good : Ink4;
        if (connected) ready.Text = $"You're {status!["handle"]}. This computer is {status["device"]}.";
        else if (on) ready.Text = "Connecting to Onsay";
        else ready.Text = "Getting this computer ready";
    }

    ////////// 6. TALKING TO HIM //////////

    // 6. From the list, as many to-dos one after another as you like. With one open, a reply to it, one at a time.
    void Send()
    {
        var text = box.Text.Trim();
        if (text.Length == 0 || !SignedIn || (session is not null && Busy)) return;
        box.Clear();
        if (session is not null) Ask(text);
        else Add(text);
    }

    /* 6. A new to-do: sent to /onsay in a session of its own, named here the onsay lingo's way so the list shows it at
          once, following up the one Not quite was pressed on, if any. The list follows it from there. */
    async void Add(string text)
    {
        var message = new JsonObject { ["prompt"] = text, ["session"] = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x12}{Compat.Shared.Next(0x1000000):x6}", ["show"] = settings.ShowWork };
        if (fixing is not null) message["from"] = fixing;
        Fix(null);
        face.React("thinking");
        try
        {
            await ReadKey();
            using var ask = To(HttpMethod.Post, "", message);
            var sending = Http.SendAsync(ask);
            await Task.Delay(300);
            List();
            using var res = await sending;
            if (!res.IsSuccessStatusCode)
                Tell($"{JsonNode.Parse(await res.Content.ReadAsStringAsync())?["error"] ?? "Something went wrong there. Try again in a moment."}");
        }
        catch (Exception) { Tell("Onsay's helper on this computer isn't answering. It starts again by itself, so try again in a moment."); }
        face.React("answered");
        List();
        Trial();
    }

    /* 6. Every to-do, as the onsay lingo lists them: drawn again when anything changed, and the open one loaded again once
          its status moves on, unless it is followed already. */
    async void List()
    {
        if (listing) return;
        listing = true;
        try
        {
            if (promptKey is null) await ReadKey();
            using var res = await Http.SendAsync(To(HttpMethod.Get, "/sessions"));
            var text = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode && text != listed && JsonNode.Parse(text) is JsonArray all)
            {
                listed = text;
                items = all;
                ReadyInput();
                Rows();
                var open = items.FirstOrDefault(i => $"{i?["session"]}" == session);
                if (session is not null && !waiting && watched != chat && $"{open?["status"]}" != loaded) Load(session);
            }
        }
        catch (Exception) { }
        listing = false;
    }

    // 6. The newest at the top, and the list goes back to the top whenever a new one comes.
    void Rows()
    {
        listRows.Children.Clear();
        empty.Visibility = Shown(items.Count == 0);
        for (int i = 0; i < items.Count; i++)
            if (items[i] is JsonObject item) listRows.Children.Add(Item(item));
        var top = items.Count > 0 ? $"{items[0]?["session"]}" : null;
        if (top != newest) listPage.ScrollToHome();
        newest = top;
    }

    /* 6. One to-do, always the same height: what you asked, and under it how far it has got, the step it is on, what it
          waits for, or the first line of its answer, each one line. Only its screens, in a row along its bottom, add to
          it. A click opens it, and a done one has Not quite. */
    Border Item(JsonObject item)
    {
        var id = $"{item["session"]}";
        var now = $"{item["status"]}";
        var answer = $"{item["text"]}".Trim();
        var device = $"{item["waiting"]}";
        var (icon, tint, line) = now switch
        {
            "working" => (WorkingIcon, Accent, $"{item["step"] ?? "Working on it"}"),
            "asking" => (AskingIcon, Accent, "Needs you"),
            "waiting" => (WaitingIcon, Ink3, device.Length > 0 ? $"Waiting for {device}" : "Waiting to reach Onsay"),
            _ => (DoneIcon, Good, answer.Split('\n')[0]),
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = $"{item["prompt"]}", FontSize = 13.5, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        body.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = now == "asking" ? Accent : Ink3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        var strip = ScreensWindow.Strip(id, 48, Named);
        strip.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(strip);
        var mark = new TextBlock
        {
            Text = icon, FontFamily = Icons, FontSize = 14, Foreground = tint, Margin = new Thickness(0, 2, 10, 0),
            VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(),
        };
        // A working one's mark turns while it works.
        if (now == "working")
            mark.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
        var row = new DockPanel { Children = { mark } };
        if (now == "done")
        {
            var redo = Right(Button(RedoIcon, Kind.Icon, () => Fix(item), "Not quite: make a new to-do that follows up this one"));
            redo.Margin = new Thickness(8, 0, 0, 0);
            redo.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(redo);
        }
        row.Children.Add(body);
        var shown = new Border { Child = row, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2) };
        Hover(shown, Page_, Brushes.Transparent);
        shown.MouseLeftButtonUp += (_, _) => Load(id);
        return shown;
    }

    // 6. Not quite: the next to-do follows up this one, carrying all of it, until it is sent or put away. null puts it away.
    void Fix(JsonObject? item)
    {
        fixing = item is null ? null : $"{item["session"]}";
        fixingText.Text = $"Not quite: {item?["prompt"]}";
        fixingChip.Visibility = Shown(fixing is not null);
        ReadyInput();
        if (fixing is not null) box.Focus();
    }

    // 6. A to-do's name by its session, what was asked, for the screens it opened.
    string Named(string id) => $"{items.FirstOrDefault(i => $"{i?["session"]}" == id)?["prompt"] ?? "A to-do"}";

    /* 6. One to-do open: its whole conversation as the onsay lingo keeps it, each prompt, then its reply, or the questions
          it ended with as a card. One working or asking shows what it does as it happens, until it stops and the list
          loads it again. */
    async void Load(string id)
    {
        Clear();
        showingScreens = false;
        session = id;
        screensBar.Child = ScreensWindow.Strip(id, 64, Named);
        int mine = chat;
        watched = mine;   // while it loads and runs, the list never loads it again over itself
        var item = items.FirstOrDefault(i => $"{i?["session"]}" == id);
        loaded = $"{item?["status"]}";
        Page();
        JsonArray turns;
        try
        {
            using var res = await Http.SendAsync(To(HttpMethod.Get, $"/sessions/{id}"));
            turns = JsonNode.Parse(await res.Content.ReadAsStringAsync())?["turns"] as JsonArray ?? new();
        }
        catch (Exception) { turns = new(); }
        if (mine != chat) return;
        if (items.FirstOrDefault(i => $"{i?["session"]}" == $"{item?["from"]}") is { } before)
        {
            var up = new TextBlock { Text = $"Not quite: {before["prompt"]}", FontSize = 12, Foreground = Ink3, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis };
            up.MouseLeftButtonUp += (_, _) => Load($"{before["session"]}");
            lines.Children.Add(up);
        }
        for (int i = 0; i < turns.Count; i++)
        {
            Yours($"{turns[i]?["prompt"]}");
            if (turns[i]?["questions"] is JsonArray questions && i == turns.Count - 1 && loaded == "asking") Questions(questions);
            else if (turns[i]?["questions"] is JsonArray asked) His().Document = Markdown.Render(string.Join("\n\n", asked.Select(q => q is JsonObject o ? $"{o["question"]}" : $"{q}")));
            else His().Document = Markdown.Render($"{turns[i]?["text"]}");
        }
        if (turns.Count == 0) Yours($"{item?["prompt"]}");
        if (loaded == "waiting")
        {
            var device = $"{item?["waiting"]}";
            His().Document = Markdown.Render(device.Length > 0 ? $"Waiting for {device}. I'll do it as soon as {device} is on." : "Waiting to reach Onsay. I'll do it as soon as this computer is back online.");
        }
        chatPage.ScrollToEnd();
        if (loaded is not ("working" or "asking"))
        {
            watched = -1;
            return;
        }
        Working($"{item?["step"] ?? "Working on it"}");
        await Follow(mine, Steps(), CancellationToken.None, untilIdle: true);
        if (mine != chat) return;
        watched = -1;
        Working(null);
    }

    /* 6. What the open to-do's prompt is doing, asked every moment until done is cancelled, it is left, or, untilIdle, it
          stops running: each new step a line in steps, the newest on the bar, and what it asks a card, answered on the
          same card. Gives the newest card. */
    async Task<Border?> Follow(int mine, StepList steps, CancellationToken done, bool untilIdle)
    {
        string? asked = null;
        Border? card = null;
        while (!done.IsCancellationRequested && mine == chat)
        {
            try
            {
                await Task.Delay(400, done);
                using var look = To(HttpMethod.Get, $"/sessions/{session}/live");
                using var res = await Http.SendAsync(look, done);
                if (JsonNode.Parse(await res.Content.ReadAsStringAsync()) is not JsonObject now || mine != chat) continue;
                if (untilIdle && (bool?)now["running"] != true) break;
                var got = now["steps"] as JsonArray ?? new();
                Hear(steps, got);
                if (got.Count > 0) Working($"{got[^1]?["text"]}");
                if (now["questions"] is not JsonArray q) asked = null;
                else if (q.ToJsonString() != asked)
                {
                    asked = q.ToJsonString();
                    card = Questions(q, answers =>
                    {
                        var body = new JsonObject { ["answers"] = Strings(answers) };
                        _ = Http.SendAsync(To(HttpMethod.Post, $"/sessions/{session}/answers", body));
                    });
                }
            }
            catch (Exception) { }
        }
        return card;
    }

    // 6. Lines for the steps the onsay lingo kept that aren't drawn yet.
    void Hear(StepList steps, JsonArray all)
    {
        for (; steps.Heard < all.Count; steps.Heard++) AddStep(steps, $"{all[steps.Heard]?["text"]}", (int?)all[steps.Heard]?["depth"] ?? 0);
    }

    /* 6. A reply to the one open: shown as yours at once, then sent to the onsay lingo's /onsay with its prompt key, in
          its session, followed as it runs (Follow). Its answer is his reply, or
          questions back. */
    async void Ask(string text)
    {
        int mine = chat;
        waiting = true;
        stopping = false;
        Yours(text);
        Page();
        following = true;
        chatPage.ScrollToEnd();
        Working("Sending your message");
        face.React("thinking");
        var started = DateTime.Now;
        JsonObject? answer = null;
        int code = 0;
        var steps = Steps();
        using var done = new CancellationTokenSource();
        Task<Border?>? followed = null;
        try
        {
            await ReadKey();
            followed = Follow(mine, steps, done.Token, untilIdle: false);
            var message = new JsonObject { ["prompt"] = text, ["session"] = session };
            using var ask = To(HttpMethod.Post, "", message);
            using var res = await Http.SendAsync(ask);
            code = (int)res.StatusCode;
            answer = JsonNode.Parse(await res.Content.ReadAsStringAsync()) as JsonObject;
        }
        catch (Exception) { }
        done.Cancel();
        var card = followed is null ? null : await followed;
        if (mine != chat) return;
        waiting = false;
        Working(null);
        if (card is not null) card.IsEnabled = false;
        // The answer has every step, for those that came while this computer was too busy to say.
        if (answer?["steps"] is JsonArray every) Hear(steps, every);
        var ms = (DateTime.Now - started).TotalMilliseconds;
        Fold(steps, ms);
        if (stopping) await Replied("Stopped.", "Stopped", ms);
        else if (code == 0) await Replied("Onsay's helper on this computer isn't answering. It starts again by itself, so try again in a moment.", "Failed", ms);
        else if (code != 200) await Replied($"{answer?["error"] ?? "Something went wrong there. Try again in a moment."}", "Failed", ms);
        else
        {
            session = $"{answer!["session"]}";
            if (answer["questions"] is JsonArray questions) Questions(questions);
            else if (answer["waiting"] is not null) await Replied(answer["waiting"]!.ToString().Length > 0 ? $"Waiting for {answer["waiting"]}. I'll do it as soon as it's on." : "Waiting to reach Onsay. I'll do it as soon as this computer is back online.", "Waiting", ms);
            else await Replied($"{answer["text"]}", "Done", ms);
        }
        // What the list says of it now, so the reply just drawn is never loaded again over itself.
        loaded = "done";
        if (answer?["questions"] is not null) loaded = "asking";
        if (answer?["waiting"] is not null) loaded = "waiting";
        List();
        Trial();
    }

    /* 6. When he thinks with Onsay's own AI, with no key or service of the person's own set in the ai lingo, Onsay is asked
          where its free trial stands after each message. Once it is over, a card offers the subscription, on Onsay's
          website, or a key of their own, once each time the app runs. */
    async void Trial()
    {
        if (offeredSubscription) return;
        var fields = await Task.Run(() => Core.SettingsFor("ai"));
        bool theirs = fields.Any(f =>
        {
            var name = $"{f?["name"]}";
            var value = f?["value"];
            if (value is null || $"{value}".Length == 0) return false;
            return name.EndsWith("_key") || name == "url" || (name == "service" && $"{value}" != "onsay");
        });
        if (theirs) return;
        var (ai, _) = await Task.Run(() => Account.Onsay("ai"));
        if ((bool?)ai?["over"] != true || offeredSubscription) return;
        offeredSubscription = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var subscribe = Button("Subscribe", Kind.Primary, () => Native.Open(SubscribePage));
        var key = Button("Bring your own key or agent", Kind.Quiet, () => SettingsWindow.Open(settings, this, "Apps"));
        key.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(subscribe);
        buttons.Children.Add(key);
        var title = new TextBlock { Text = "Your free month of Onsay AI is over", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
        var card = new StackPanel { Children = { title, Paragraph("Subscribe to keep his AI and your computers on the web, or bring your own key or agent."), buttons } };
        (session is null ? asksPanel : lines).Children.Add(new Border
        {
            Child = card,
            Background = Page_,
            BorderBrush = Rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 14),
            Margin = new Thickness(0, 2, 24, 12),
        });
        chatPage.ScrollToEnd();
        listPage.ScrollToHome();
    }

    // 6. The chat key, which the app's /onsay asks for (Host.cs).
    Task ReadKey()
    {
        promptKey = Host.PromptKey;
        return Task.CompletedTask;
    }

    // 6. A request to the onsay lingo's /onsay, with its prompt key as each message last read it.
    HttpRequestMessage To(HttpMethod method, string path, JsonObject? body = null)
    {
        var req = new HttpRequestMessage(method, $"{Host.Url}/onsay{path}");
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
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
        face.React(ended switch
        {
            "Done" => "answered",
            "Stopped" => "no",
            _ => "stuck",
        });
    }

    ////////// 7-8. THE BAR AND THE BANNER //////////

    // 7. Each dot fades up and down a beat after the one before it. The timer counts from when the current text began.
    void Working(string? text)
    {
        ReadyInput();
        bool was = working.Visibility == Visibility.Visible;
        if (text is not null && text != workingText.Text) doingSince = DateTime.Now;
        working.Visibility = Shown(text is not null);
        workingText.Text = text ?? "";
        workingTime.Text = text is null ? "" : Took((DateTime.Now - doingSince).TotalMilliseconds, live: true);
        if (text is null) tick.Stop();
        else tick.Start();
        // Asked again with the bar already showing, the dots keep their beat.
        if (was == (text is not null)) return;
        for (int i = 0; i < dots.Length; i++)
        {
            if (text is null)
            {
                dots[i].BeginAnimation(OpacityProperty, null);
                continue;
            }
            dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(420))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(i * 160),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
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
        if (Showing)
        {
            await face.Speak(text, soFar =>
            {
                if ((DateTime.Now - drawn).TotalMilliseconds < 50) return;
                drawn = DateTime.Now;
                row.Document = Markdown.Render(soFar);
            }, Math.Max(46, text.Length / 4.0));
        }
        if (speaking?.row == row)
        {
            row.Document = Markdown.Render(text);
            speaking = null;
        }
    }

    // Yours sit right on blue, as you typed it.
    void Yours(string text)
    {
        var shown = Selectable(14, Ink);
        shown.Text = text;
        lines.Children.Add(new Border
        {
            Child = shown,
            Background = BlueSoft,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 7, 12, 8),
            Margin = new Thickness(48, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Right,
        });
    }

    // His sit left on the card, the whole width, read only but selectable, with its links and Copy buttons live.
    RichTextBox His()
    {
        var shown = new RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Foreground = Ink,
            Margin = new Thickness(0, 2, 0, 12),
            Document = new System.Windows.Documents.FlowDocument(),
        };
        lines.Children.Add(shown);
        return shown;
    }

    // How long, the way a person reads it: 0.8s, 12s, 1m 05s. Live, it counts whole seconds.
    static string Took(double ms, bool live = false)
    {
        var s = ms / 1000;
        if (s >= 60) return $"{(int)(s / 60)}m {(int)(s % 60):00}s";
        if (live) return $"{Math.Floor(s)}s";
        if (s < 10) return $"{s:0.0}s";
        return $"{Math.Round(s)}s";
    }

    // Answers as JSON, for the lingo client or /onsay.
    static JsonArray Strings(IEnumerable<string> texts) => new(texts.Select(t => (JsonNode?)t).ToArray());

    ////////// 10. QUESTIONS BACK //////////

    /* 10. Each question is text, or {question, why, choices, other, skip}: picked, or typed under Something else, or Skip,
           which sends Skipped. other and skip false leave those out. Send lights up once
           every one is answered, and sends them all as the next message in this session, a line each: the question, then
           its answer. Or, asked while a message runs, to send, its answers in order. Once sent, the card keeps only each
           question and its answer. Gives the card, in the conversation or into another panel. */
    Border Questions(JsonArray questions, Action<string[]>? send = null, Panel? into = null)
    {
        var asked = questions.Select(q =>
        {
            if (q is JsonObject o) return (words: $"{o["question"] ?? o["text"]}", choices: o["choices"] as JsonArray);
            return (words: $"{q}", choices: null);
        }).ToList();
        var answers = new string[asked.Count];
        var picked = new string[asked.Count];
        const string Other = "\u0001other";
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
            var q = questions[at] as JsonObject;
            if (!string.IsNullOrWhiteSpace($"{q?["why"]}"))
            {
                card.Children.Add(new TextBlock
                {
                    Text = $"{q!["why"]}",
                    FontSize = 12,
                    Foreground = Ink2,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }
            var options = (asked[i].choices?.Select(c => ($"{c}", $"{c}")) ?? Enumerable.Empty<(string, string)>()).ToList();
            bool choosing = options.Count > 0;
            if (choosing && (bool?)q?["other"] != false) options.Add(("Something else", Other));
            if ((bool?)q?["skip"] != false) options.Add(("Skip", "Skipped"));
            var typed = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
            typed.TextChanged += (_, _) =>
            {
                answers[at] = typed.Text;
                picked[at] = choosing ? Other : "";
                Ready();
            };
            var typedBox = new Border
            {
                BorderBrush = Edge_,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 6, 4),
                Background = Card,
                Child = typed,
                Visibility = Shown(!choosing),
            };
            if (options.Count > 0)
            {
                card.Children.Add(SetupWindow.Chips(options.ToArray(), () => picked[at] ?? "", v =>
                {
                    picked[at] = v;
                    typedBox.Visibility = Shown(v == Other || (!choosing && v != "Skipped"));
                    answers[at] = v == Other ? typed.Text : v;
                    Ready();
                }));
            }
            card.Children.Add(typedBox);
        }
        go = Button("Send", Kind.Primary, () =>
        {
            if (send is null && Busy) return;
            var told = asked.Select((q, i) =>
            {
                var words = q.words.TrimEnd();
                bool punctuated = words.EndsWith('?') || words.EndsWith(':');
                return $"{q.words}{(punctuated ? "" : ":")} {answers[i].Trim()}";
            }).ToList();
            card.Children.Clear();
            foreach (var line in told)
            {
                card.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 13,
                    Foreground = Ink2,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }
            if (send is not null) send(answers.Select(a => a.Trim()).ToArray());
            else Ask(string.Join("\n", told));
        });
        go.Margin = new Thickness(0, 14, 0, 0);
        card.Children.Add(go);
        Ready();
        var shown = new Border
        {
            Child = card,
            Background = Page_,
            BorderBrush = Rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 14),
            Margin = new Thickness(0, 2, 24, 12),
        };
        (into ?? lines).Children.Add(shown);
        face.Mood("calm");
        face.React("answered");
        return shown;
    }

    /* 10. Every second, the questions lingos wait on the person for, asked off this thread, since the lingo client answers
           one request at a time. Asking is also how the lingo client knows someone is here to answer. */
    public void Poll()
    {
        bool asking = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (asking) return;
            asking = true;
            var all = await Task.Run(() => ToDos.Asks());
            asking = false;
            Asked(all.ToList());
        };
        timer.Start();
    }

    /* 10. The questions a lingo waits on the person for (chat.ask), as the lingo client keeps them: each a card here, the
           chat opening for it, its answers sent back to the lingo client in order. A card whose question is no longer
           waited for, answered or given up on, is left as it is and can't be sent. Under the list while it shows. */
    public void Asked(List<JsonNode?> waiting)
    {
        var gone = lingoCards.Where(c => !waiting.Any(a => (double?)a?["id"] == c.Key)).ToList();
        foreach (var (id, card) in gone)
        {
            card.IsEnabled = false;
            lingoCards.Remove(id);
        }
        foreach (var a in waiting)
        {
            if ((double?)a?["id"] is not { } id || lingoCards.ContainsKey(id) || a["questions"] is not JsonArray questions) continue;
            if (!Showing) setOpen(true);
            lingoCards[id] = Questions(questions, answers =>
            {
                ToDos.Answer((long)id, null, Strings(answers));
            }, session is null ? asksPanel : lines);
            listPage.ScrollToHome();
            Page();
        }
    }

    /* 10. What a lingo says while it works (chat.say), his line in the chat, or a step it shows (chat.step): under the
           message running, a step further in than Onsay's own, and on the bar, else with the steps that came on their
           own. Neither opens the chat, so the work never loses the keyboard. While the list shows, what it says is the
           banner, and its steps are the rows'. */
    public void Lingo(string kind, string text)
    {
        if (session is null)
        {
            if (kind == "say") Tell(text);
            return;
        }
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
            Head = head,
            Body = body,
            Root = new Border
            {
                BorderBrush = Rule,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 2, 0, 2),
                Margin = new Thickness(0, 0, 8, 12),
                Visibility = Visibility.Collapsed,
                Child = new StackPanel { Children = { head, body } },
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
            FontSize = 12,
            Foreground = Ink2,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Math.Min(depth, 4) * 14, 2, 0, 2),
            Inlines = { new System.Windows.Documents.Run("●  ") { Foreground = depth > 0 ? Ink4 : Accent, FontSize = 8 }, new System.Windows.Documents.Run(text) },
        });
    }

    // 10. Once answered it folds under one line, "4 steps, 3.1s", or stays open, as he was last told.
    void Fold(StepList list, double ms)
    {
        list.Ms = ms;
        if (list.Count == 0)
        {
            lines.Children.Remove(list.Root);
            stepLists.Remove(list);
            return;
        }
        list.Head.Visibility = Visibility.Visible;
        list.Head.Inlines.Clear();
        list.Head.Inlines.Add(new System.Windows.Documents.Run(StepsOpen ? OpenIcon : FoldedIcon) { FontFamily = Icons, FontSize = 9 });
        list.Head.Inlines.Add(new System.Windows.Documents.Run($"  {list.Count} step{(list.Count == 1 ? "" : "s")}, {Took(ms)}"));
        list.Body.Visibility = Shown(StepsOpen);
    }

    ////////// 11. BACK TO THE LIST //////////

    // 11. An answer still on its way to the one that was open is dropped when it comes.
    void Clear()
    {
        lines.Children.Clear();
        stepLists.Clear();
        foreach (var card in lingoCards.Values) (card.Parent as Panel)?.Children.Remove(card);
        lingoCards.Clear();
        session = null;
        screensBar.Child = null;
        waiting = false;
        chat++;
        Working(null);
        Page();
    }

    ////////// 12. THE MENUS //////////

    // A row in a menu: a title, and a quieter line under it.
    static Border Row(string text, string under, Action click)
    {
        var row = new Border
        {
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(7),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = text, FontSize = 13, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = under, FontSize = 11.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
        Hover(row, Sunk, Brushes.Transparent);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    // 12. A small menu under a button, lifted like the card, closed by clicking anywhere else.
    static Popup Pop(FrameworkElement anchor, UIElement content)
    {
        // Under the button, its right edge with the button's, so it opens into the card.
        var menu = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (size, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width - size.Width + Shadow / 2, target.Height + 6), PopupPrimaryAxis.Horizontal) },
            StaysOpen = false,
            AllowsTransparency = true,
            Child = Lifted(new Border { Padding = new Thickness(6), MinWidth = 220, MaxWidth = 300, Child = content }, Shadow / 2),
        };
        menu.IsOpen = true;
        return menu;
    }

    // 12. The header's menu: Copy chat, from the session, and Apps and settings.
    void Menu(FrameworkElement anchor)
    {
        var rows = new StackPanel();
        var menu = Pop(anchor, rows);
        // The chat as the onsay lingo keeps it in its session: each message, then his reply or his questions.
        if (session is not null) rows.Children.Add(Row("Copy chat", "The whole chat, as text", async () =>
        {
            menu.IsOpen = false;
            try
            {
                using var res = await Http.SendAsync(To(HttpMethod.Get, $"/sessions/{session}"));
                var turns = JsonNode.Parse(await res.Content.ReadAsStringAsync())?["turns"] as JsonArray ?? new();
                var written = turns.Select(t =>
                {
                    var reply = $"{t?["text"]}";
                    if (t?["questions"] is JsonArray q) reply = string.Join("\n", q.Select(x => x is JsonObject o ? $"{o["question"]}" : $"{x}"));
                    return $"You: {t?["prompt"]}\n\nOnsay: {reply}";
                });
                Clipboard.SetText(string.Join("\n\n", written));
                Tell("Copied the chat.");
            }
            catch (Exception) { Tell("The chat couldn't be copied. Try again in a moment."); }
        }));
        rows.Children.Add(Row("Apps and settings", "What your computers offer, people, activity and account", () =>
        {
            menu.IsOpen = false;
            SettingsWindow.Open(settings, this);
        }));
    }

    /* 12. Where the chat sits, as four small pictures of a screen, the way a browser's developer tools offer theirs: beside
           him, or docked left, at the bottom or right. The one in use is filled in. Dragging the top bar to a side of the
           screen does the same, which the line under them says. */
    void SitMenu(FrameworkElement anchor)
    {
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 0) };
        var all = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Where the chat sits", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(6, 2, 6, 8) },
                tiles,
                new TextBlock
                {
                    Text = "Or drag its top bar to a side of your screen. Docked, it keeps that side for itself, so your windows make room.",
                    FontSize = 11.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 6, 4),
                },
            },
        };
        var menu = Pop(anchor, all);
        foreach (var (edge, name) in new (Edge?, string)[] { (null, "Next to him"), (Edge.Left, "Left"), (Edge.Bottom, "Bottom"), (Edge.Right, "Right") })
        {
            bool on = edge == Docked;
            var tint = on ? Accent : Ink4;
            var screen = new Grid { Width = 48, Height = 32 };
            screen.Children.Add(new Border { BorderBrush = on ? Accent : Edge_, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4) });
            var part = new Border { Background = tint, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(4) };
            if (edge == Edge.Left) part.Width = 14;
            if (edge == Edge.Right) part.Width = 14;
            if (edge == Edge.Bottom) part.Height = 9;
            part.HorizontalAlignment = edge switch
            {
                Edge.Left => HorizontalAlignment.Left,
                Edge.Right => HorizontalAlignment.Right,
                null => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Stretch,
            };
            part.VerticalAlignment = edge is Edge.Bottom or null ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            if (edge is null)
            {
                // Beside him: a small card, and him under it.
                part.Width = 12;
                part.Height = 13;
                part.Margin = new Thickness(0, 0, 9, 10);
                screen.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = tint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 3) });
            }
            screen.Children.Add(part);
            var tile = new Border
            {
                Padding = new Thickness(8, 8, 8, 6),
                Margin = new Thickness(0, 0, 2, 0),
                CornerRadius = new CornerRadius(7),
                Cursor = Cursors.Hand,
                Background = on ? BlueSoft : Brushes.Transparent,
                Child = new StackPanel
                {
                    Children =
                    {
                        screen,
                        new TextBlock { Text = name, FontSize = 11.5, Foreground = on ? Ink : Ink2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) },
                    },
                },
            };
            if (!on) Hover(tile, Sunk, Brushes.Transparent);
            tile.MouseLeftButtonUp += (_, _) =>
            {
                menu.IsOpen = false;
                Sit(edge);
            };
            tiles.Children.Add(tile);
        }
    }

    ////////// 13. THE LOOK EVERY WINDOW SHARES //////////

    // brand.css's tokens, light theme, the website's own. Every window draws with these, through using static
    // Onsay.Desktop.ChatWindow.
    internal const string Brand = "#5865F2";
    internal static readonly SolidColorBrush Card = Hex("#FFFFFF");
    internal static readonly SolidColorBrush Page_ = Hex("#F5F5FA");
    internal static readonly SolidColorBrush Sunk = Hex("#EEEEF5");
    internal static readonly SolidColorBrush Edge_ = Hex("#D8D9E5");
    internal static readonly SolidColorBrush Rule = Hex("#E8E8F0");
    internal static readonly SolidColorBrush Ink = Hex("#12131C");
    internal static readonly SolidColorBrush Ink2 = Hex("#4A4D5E");
    internal static readonly SolidColorBrush Ink3 = Hex("#7B7F92");
    internal static readonly SolidColorBrush Ink4 = Hex("#A9ACBC");
    internal static readonly SolidColorBrush DropSoft = Hex("#D9DAEC");
    internal static readonly SolidColorBrush BlueSoft = Hex("#ECEEFE");
    internal static readonly SolidColorBrush Accent = Hex(Brand);
    internal static readonly SolidColorBrush AccentDeep = Hex("#4046C9");
    internal static readonly SolidColorBrush AccentEdge = Hex("#353DB8");
    internal static readonly SolidColorBrush Good = Hex("#12A594");
    internal static readonly SolidColorBrush Bad = Hex("#C62A2F");
    internal static readonly SolidColorBrush BadSoft = Hex("#FDECEC");
    internal static readonly SolidColorBrush BadEdge = Hex("#F5C2C4");
    internal static readonly SolidColorBrush Butter = Hex("#FFF6D6");
    internal static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    // The website's fonts, shipped in fonts/: Gabarito for headings, and IBM Plex Sans and Mono for words and code, renamed
    // Onsay Sans and Onsay Mono for their license (fonts/OFL.txt).
    internal static readonly FontFamily Display = Font("Gabarito");
    internal static readonly FontFamily Ui = Font("Onsay Sans");
    internal static readonly FontFamily Mono = Font("Onsay Mono");

    static FontFamily Font(string name) => new(new Uri("pack://application:,,,/"), $"./fonts/#{name}, Segoe UI");

    internal static SolidColorBrush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    internal static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    // Docks an element to the right of its DockPanel, and gives it back.
    internal static T Right<T>(T e) where T : UIElement
    {
        DockPanel.SetDock(e, System.Windows.Controls.Dock.Right);
        return e;
    }

    internal static void Hover(Border b, Brush over, Brush rest)
    {
        b.MouseEnter += (_, _) => b.Background = over;
        b.MouseLeave += (_, _) => b.Background = rest;
    }

    // The same animation across and down, or null to let go of both.
    internal static void ScaleBoth(ScaleTransform scale, DoubleAnimation? animation)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /* The brand's lifted look, for a card that floats: an edge, and a solid shadow offset down and right, never blurred.
       It costs nothing to draw however much moves in the card. Gives the card with its shadow, offset by that much. */
    internal static Grid Lifted(Border inner, double offset)
    {
        inner.Background = Card;
        inner.BorderBrush = Edge_;
        inner.BorderThickness = new Thickness(1);
        inner.CornerRadius = new CornerRadius(10);
        var drop = new Border { Background = DropSoft, CornerRadius = new CornerRadius(10), Margin = new Thickness(offset, offset, 0, 0) };
        inner.Margin = new Thickness(0, 0, offset, offset);
        return new Grid { Children = { drop, inner } };
    }

    // Every kind of button is the same shape: a rounded rectangle, never a pill. Ghost is words alone, Icon a glyph alone.
    internal enum Kind { Primary, Quiet, Danger, Ghost, Icon }

    /* Darker under the pointer and pressed in while held. The primary one is filled in the brand colour on a hard shadow,
       the way the website's is. Ghost and Icon have no border and darken to ink under the pointer. A click needs both
       press and release on it. */
    internal static Border Button(string text, Kind kind, Action click, string? tip = null)
    {
        bool icon = kind == Kind.Icon;
        bool primary = kind == Kind.Primary;
        var (rest, hover, edge, ink) = kind switch
        {
            Kind.Primary => ((Brush)Accent, (Brush)AccentDeep, (Brush)AccentEdge, Brushes.White),
            Kind.Danger => (Card, BadSoft, BadEdge, Bad),
            Kind.Icon or Kind.Ghost => (Brushes.Transparent, Sunk, Edge_, Ink3),
            _ => (Card, Sunk, Edge_, Ink),
        };
        bool bare = kind is Kind.Icon or Kind.Ghost;
        var scale = new ScaleTransform();
        var b = new Border
        {
            Background = rest,
            CornerRadius = new CornerRadius(7),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = edge,
            BorderThickness = new Thickness(bare ? 0 : 1),
            Padding = icon ? new Thickness(0) : new Thickness(bare ? 8 : 14, 6, bare ? 8 : 14, 7),
            Width = icon ? 32 : double.NaN,
            Height = icon ? 32 : double.NaN,
            Effect = primary ? new DropShadowEffect { BlurRadius = 0, ShadowDepth = 2.83, Direction = 315, Color = AccentEdge.Color, Opacity = 1 } : null,
            Child = new TextBlock
            {
                Text = text,
                FontSize = icon ? 14 : 13,
                FontWeight = bare ? FontWeights.Normal : FontWeights.SemiBold,
                FontFamily = icon ? Icons : Ui,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ink,
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        bool down = false;
        void Press(bool on)
        {
            scale.ScaleX = on ? 0.95 : 1;
            scale.ScaleY = on ? 0.95 : 1;
        }
        Hover(b, hover, rest);
        var words = (TextBlock)b.Child;
        if (bare)
        {
            b.MouseEnter += (_, _) => words.Foreground = Ink;
            b.MouseLeave += (_, _) => words.Foreground = ink;
        }
        b.MouseLeave += (_, _) =>
        {
            down = false;
            Press(false);
        };
        b.MouseLeftButtonDown += (_, e) =>
        {
            down = true;
            Press(true);
            e.Handled = true;
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            Press(false);
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
        Text = text,
        FontSize = 13,
        Foreground = Ink2,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, centre ? 16 : 10),
        TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left,
        HorizontalAlignment = centre ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        MaxWidth = centre ? 320 : double.PositiveInfinity,
    };

    // A heading over a few things, like a setting's name.
    internal static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 13.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 14, 0, 6),
    };

    // A quieter line, under or beside something. Empty, it takes no room, so one waiting to say how something went is unseen.
    internal static TextBlock Note(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Ink3,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
        Style = Unseen,
    };

    static readonly Style Unseen = new(typeof(TextBlock))
    {
        Triggers =
        {
            new Trigger { Property = TextBlock.TextProperty, Value = "", Setters = { new Setter(VisibilityProperty, Visibility.Collapsed) } },
        },
    };

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
            if (animate && SystemParameters.ClientAreaAnimation)
            {
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() });
            }
            else
            {
                move.BeginAnimation(TranslateTransform.XProperty, null);
                move.X = to;
            }
        }
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            on = !on;
            Draw(true);
            changed(on);
        };
        Draw(false);
        return track;
    }

    // A setting that is on or off, as a card: its words on the left and its Toggle on the right. A click anywhere flips it.
    internal static Border Switch(string text, string? under, bool on, Action<bool> changed)
    {
        var track = Toggle(on, changed);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap });
        if (under is not null)
        {
            words.Children.Add(new TextBlock { Text = under, FontSize = 12.5, Foreground = Ink3, Margin = new Thickness(0, 2, 12, 0), TextWrapping = TextWrapping.Wrap });
        }
        var row = new Border
        {
            Background = Card,
            BorderBrush = Edge_,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand,
            Child = new DockPanel { Children = { Right(track), words } },
        };
        Hover(row, Page_, Card);
        row.MouseLeftButtonUp += (_, _) => track.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
        return row;
    }

    // Read only text that can still be selected and copied.
    internal static TextBox Selectable(double size, Brush ink) => new()
    {
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(0),
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        Foreground = ink,
    };

    // A right click menu in the brand's look: a lifted card of plain rows that darken under the pointer. Its style is
    // "Menu", and its rows and lines take theirs from it as its Resources.
    internal static readonly ResourceDictionary Menus = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse($"""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="Menu" TargetType="ContextMenu">
            <Setter Property="OverridesDefaultStyle" Value="True" />
            <Setter Property="HasDropShadow" Value="False" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                  <Grid>
                    <Border Background="{DropSoft.Color}" CornerRadius="10" Margin="4,4,0,0" />
                    <Border Background="{Card.Color}" BorderBrush="{Edge_.Color}" BorderThickness="1" CornerRadius="10" Padding="5" MinWidth="180" Margin="0,0,4,4">
                      <StackPanel IsItemsHost="True" />
                    </Border>
                  </Grid>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="MenuItem">
            <Setter Property="OverridesDefaultStyle" Value="True" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                  <Border x:Name="row" Background="Transparent" CornerRadius="7" Padding="10,6,10,7">
                    <ContentPresenter ContentSource="Header" TextBlock.FontSize="13" TextBlock.Foreground="{Ink.Color}" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="row" Property="Background" Value="{Sunk.Color}" /></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="Separator">
            <Setter Property="OverridesDefaultStyle" Value="True" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Separator"><Border Height="1" Background="{Rule.Color}" Margin="6,4" /></ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """);

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
