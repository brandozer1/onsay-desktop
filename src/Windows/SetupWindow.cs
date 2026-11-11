// SetupWindow.cs - setting Onsay up, the first time he opens after installing: he says hello, you pick how he looks and
// where to find him, read what is sent to Onsay, pick his AI, and sign in. Onsay stands on the left and says each step out
// loud, the step on the right.
//
// 1. Open(settings, chat, done)    shows it, and done runs once it is finished or skipped
// 2. Welcome, Look, Find, Sent, Ai, SignIn   the steps
// 3. LookPicker(settings, changed) his colour, face and surface, live. Also his look in Settings
// 4. Go(step)                      moves to a step, sliding it in, and he says its line
// 5. Step, Swatch, Chips           the pieces the steps are made of, beside the ones every window shares (ChatWindow.cs)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Onsay.Character;
using static Onsay.Desktop.ChatWindow;

namespace Onsay.Desktop;

sealed class SetupWindow : Window
{
    ////////// LOOK //////////

    // The colours he comes in: the brand's, then a few that suit most desktops. His surfaces are OnsayView's.
    static readonly string[] Palette = { Brand, "#3B82F6", "#12A594", "#F97316", "#E5484D", "#D946EF", "#12131C", "#FFDE59" };
    static readonly (string Value, string Name)[] Faces = { ("#FFFFFF", "Light"), ("#12131C", "Dark") };
    static readonly (string Value, string Name)[] Surfaces = OnsayView.Materials.Keys.Select(k => (k, Core.Title(k))).ToArray();

    ////////// STATE //////////

    readonly Settings settings;
    readonly ChatWindow chat;
    readonly Action done;
    readonly OnsayView him = new() { Width = 168, Height = 168 };
    readonly TextBlock said = new() { FontSize = 15, Foreground = Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MinHeight = 20 };
    readonly ContentControl body = new();
    readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Func<UIElement>[] steps;
    const int Agree = 3;   // the step about what is sent: skipping lands there, never past it
    int step = -1;

    ////////// 1. OPEN //////////

    // 1.
    public static void Open(Settings settings, ChatWindow chat, Action done) => new SetupWindow(settings, chat, done).Show();

    SetupWindow(Settings settings, ChatWindow chat, Action done)
    {
        this.settings = settings;
        this.chat = chat;
        this.done = done;
        steps = new Func<UIElement>[] { Welcome, Look, Find, Sent, Ai, SignIn };
        Title = "Set up Onsay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Width = 820;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Ui;
        Topmost = true;

        // The stage: Onsay on a dotted floor, with what he says under him.
        var dot = new DrawingBrush(new GeometryDrawing(Edge_, null, new EllipseGeometry(new Point(1, 1), 1, 1)))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 22, 22),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 22, 22),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        var stage = new Border
        {
            Width = 320,
            Background = Page_,
            CornerRadius = new CornerRadius(9, 0, 0, 9),
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid
            {
                Children =
                {
                    new Border { Background = dot, CornerRadius = new CornerRadius(9, 0, 0, 9), Opacity = 0.9 },
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 0),
                        Children =
                        {
                            him,
                            new Border
                            {
                                Background = Card,
                                BorderBrush = Edge_,
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(12),
                                Padding = new Thickness(16, 12, 16, 12),
                                Margin = new Thickness(0, 22, 0, 0),
                                Child = said,
                                Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 3, Direction = 315, Color = DropSoft.Color, Opacity = 1 },
                            },
                        },
                    },
                },
            },
        };
        DockPanel.SetDock(stage, System.Windows.Controls.Dock.Left);

        var close = Button("\uE711", Kind.Icon, Skip, "Skip for now");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 14, 16, 0);
        var bottom = new DockPanel { Margin = new Thickness(40, 0, 40, 30), Children = { Right(actions), dots } };
        DockPanel.SetDock(bottom, System.Windows.Controls.Dock.Bottom);
        var holder = new ContentControl { Content = body, Margin = new Thickness(40, 54, 40, 10) };
        var right = new DockPanel { Children = { bottom, new Grid { Children = { holder, close } } } };

        var card = new Border { Child = new DockPanel { Children = { stage, right } } };
        var lifted = Lifted(card, 6);
        lifted.Margin = new Thickness(24, 24, 18, 18);
        // Dragged by its empty background alone, never by a swatch, chip or switch, which are borders too.
        var floor = (Grid)stage.Child;
        var surfaces = new HashSet<object> { card, stage, floor, floor.Children[0], right, bottom };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (surfaces.Contains(e.OriginalSource)) DragMove();
        };
        Content = lifted;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Skip();
        };
        Loaded += (_, _) =>
        {
            him.React("greet");
            Go(0);
        };
    }

    // Before the step about what is sent, skipping goes to it. From there on it finishes.
    void Skip()
    {
        if (step < Agree) Go(Agree);
        else Finish();
    }

    // Done or skipped: what was picked is already kept, and the person meets him in his chat.
    void Finish()
    {
        if (settings.SetUp == true) return;
        settings.SetUp = true;
        settings.Save();
        Close();
        done();
    }

    ////////// 2. THE STEPS //////////

    // 2.
    UIElement Welcome()
    {
        Line("Hi, I'm Onsay.");
        Actions(Button("Let's go", Kind.Primary, () => Go(1)));
        return Step("Welcome to Onsay", "A helper who lives on your computer. Tell him what you want in plain words, and he does it. " +
            "You choose what he may do, and Alt+Shift+O stops him at once, from any app.",
            Note("Setting him up takes a minute. You can change all of it later in his settings."));
    }

    // 2.
    UIElement Look()
    {
        Line("How do I look?");
        Actions(Button("Back", Kind.Quiet, () => Go(0)), Button("Next", Kind.Primary, () => Go(2)));
        return Step("Make him yours", "Pick his colour, his face and his surface. He changes as you pick.", LookPicker(settings, () => him.React("yes")));
    }

    // 2. Shortcuts are only for a copy that was installed, since a built one has nowhere fixed to point them.
    UIElement Find()
    {
        Line("Where should I be?");
        Actions(Button("Back", Kind.Quiet, () => Go(1)), Button("Next", Kind.Primary, () => Go(3)));
        var list = new StackPanel();
        if (Updates.Installed)
        {
            bool onDesktop = Updates.Shortcut(Updates.Place.Desktop);
            bool inStart = Updates.Shortcut(Updates.Place.StartMenu);
            list.Children.Add(Switch("A shortcut on your desktop", "Open his chat from your desktop.", onDesktop, on => Updates.Shortcut(Updates.Place.Desktop, on)));
            list.Children.Add(Switch("In the Start menu", "Find him by typing Onsay in Start.", inStart, on => Updates.Shortcut(Updates.Place.StartMenu, on)));
        }
        list.Children.Add(Switch("Start with Windows", "He appears on your taskbar when you sign in.", Startup.IsOn, Startup.Set));
        return Step("Where to find him", "He always sits on your taskbar. Click him to chat, or drag him anywhere you like.", list);
    }

    // 2. One button: agreeing is how the person goes on.
    UIElement Sent()
    {
        Line("Here's what I send.");
        Actions(Button("Back", Kind.Quiet, () => Go(2)), Button("Agree and continue", Kind.Primary, () => Go(4)));
        return Step("What is sent to Onsay", "To answer you, this computer sends Onsay what you ask for, with the chat before it, and what your apps ask Onsay as they run. " +
            "Your files, passwords and screen stay here unless you ask him to send something.",
            Note("Everything sent is kept in a log you can read any time, under Advanced."));
    }

    /* 2. His AI: Onsay's own, free for 30 days from his first message with no card, is picked first. Or one they already
          have, with its key and where to get one. Next keeps the pick in the ai lingo's settings, and a service picked needs
          its key before going on. Onsay's own leaves the service unset, so a key added later is used. */
    UIElement Ai()
    {
        Line("Which AI should I think with?");
        var picked = "onsay";
        var key = new PasswordBox { BorderThickness = new Thickness(0), FontSize = 13 };
        var keyLabel = Label("");
        var getKey = Button("", Kind.Quiet, () => Native.Open(Services.First(s => s.Value == picked).Keys));
        var keyArea = new StackPanel { Visibility = Visibility.Collapsed, Children = { keyLabel, SettingsWindow.Boxed(key), getKey } };
        getKey.HorizontalAlignment = HorizontalAlignment.Left;
        getKey.Margin = new Thickness(0, 8, 0, 0);
        var free = Note("Free for your first 30 days, no card needed. After that you can subscribe, or add a key of your own any time in his settings.");
        var problem = Note("");
        problem.Foreground = Bad;
        problem.Margin = new Thickness(0, 10, 0, 0);

        void Show()
        {
            var s = Services.First(x => x.Value == picked);
            free.Visibility = picked == "onsay" ? Visibility.Visible : Visibility.Collapsed;
            keyArea.Visibility = picked == "onsay" ? Visibility.Collapsed : Visibility.Visible;
            keyLabel.Text = $"Your {s.Name} API key";
            ((TextBlock)getKey.Child).Text = $"Get a {s.Name} key";
            problem.Text = "";
        }

        void Next()
        {
            var s = Services.First(x => x.Value == picked);
            var typed = key.Password.Trim();
            if (picked != "onsay" && typed.Length == 0)
            {
                problem.Text = $"Paste your {s.Name} key to go on, or pick Onsay AI.";
                return;
            }
            var why = Core.Set("ai", "service", picked == "onsay" ? null : picked);
            if (why is null && picked != "onsay") why = Core.Set("ai", s.Setting, typed);
            if (why is not null)
            {
                problem.Text = why;
                return;
            }
            Go(5);
        }

        Actions(Button("Back", Kind.Quiet, () => Go(3)), Button("Next", Kind.Primary, Next));
        var chips = Chips(Services.Select(s => (s.Value, s.Name)).ToArray(), () => picked, v =>
        {
            picked = v;
            Show();
        });
        return Step("His AI", "He thinks with an AI. Start free with Onsay's, or use one you already have.",
            new StackPanel { Children = { chips, free, keyArea, problem } });
    }

    // The AIs he can think with from setting up: Onsay's, then the popular ones, each with the ai lingo's setting its key
    // goes in and where a key is made. More are in ai's settings, under Apps.
    static readonly (string Value, string Name, string Setting, string Keys)[] Services =
    {
        ("onsay", "Onsay AI, 30 days free", "", ""),
        ("chatgpt", "ChatGPT", "openai_key", "https://platform.openai.com/api-keys"),
        ("claude", "Claude", "claude_key", "https://console.anthropic.com/settings/keys"),
        ("gemini", "Gemini", "gemini_key", "https://aistudio.google.com/apikey"),
        ("grok", "Grok", "grok_key", "https://console.x.ai"),
    };

    // 2. Signed in, here or before, it is the last word: all set.
    UIElement SignIn()
    {
        if (Account.Onsay("status").answer is { } status && (bool?)status["signed_in"] == true)
        {
            Line("All set. Click me any time.");
            him.React("celebrate");
            Actions(Button("Meet Onsay", Kind.Primary, Finish));
            var who = status["handle"] is { } handle ? $"You're {handle}." : "You're signed in.";
            return Step("You're all set", $"{who} He's on your taskbar now: click him any time to open his chat.", new Border());
        }
        Line("Let's sign you in.");
        Actions(Button("Back", Kind.Quiet, () => Go(4)), Button("Skip for now", Kind.Quiet, Finish));
        return Step("Sign in", "Signing in gives this computer its own name on Onsay, like laptop.yourname, so he can answer you in the chat, and turns on Onsay's AI.",
            chat.SignInBox(() => Go(5)));
    }

    ////////// 3. HIS LOOK //////////

    /* 3. Every pick paints every one of him at once and is kept. changed runs after each, for a reaction. The same picker
          is the Look page in Settings. */
    public static UIElement LookPicker(Settings settings, Action? changed = null)
    {
        var look = OnsayView.Current;
        var panel = new StackPanel();
        void Set(OnsayView.Appearance next)
        {
            OnsayView.Paint(next);
            settings.Look = next;
            settings.Save();
            changed?.Invoke();
        }

        var colours = new WrapPanel();
        foreach (var c in Palette)
        {
            colours.Children.Add(Swatch(c, () => look.Color.Equals(c, StringComparison.OrdinalIgnoreCase), () => Set(look = look with { Color = c }), colours));
        }
        panel.Children.Add(Label("Colour"));
        panel.Children.Add(colours);
        panel.Children.Add(Label("Face"));
        panel.Children.Add(Chips(Faces, () => look.Face, v => Set(look = look with { Face = v })));
        panel.Children.Add(Label("Surface"));
        panel.Children.Add(Chips(Surfaces, () => look.Material, v => Set(look = look with { Material = v })));
        return panel;
    }

    ////////// 4. MOVING BETWEEN STEPS //////////

    // 4. The new step slides in from the side it comes from, and the dots say where you are.
    void Go(int to)
    {
        bool forward = to > step;
        step = to;
        var content = steps[to]();
        var slide = new TranslateTransform(forward ? 28 : -28, 0);
        if (content is FrameworkElement f)
        {
            f.RenderTransform = slide;
            f.Opacity = 0;
        }
        body.Content = content;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 320 : 1);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, time) { EasingFunction = ease });
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, time) { EasingFunction = ease });

        dots.Children.Clear();
        for (int i = 0; i < steps.Length; i++)
        {
            Brush colour = Rule;
            if (i == to) colour = Accent;
            else if (i < to) colour = BlueSoft;
            dots.Children.Add(new Border
            {
                Width = i == to ? 22 : 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0),
                Background = colour,
            });
        }
    }

    // He says it, his mouth moving with each letter as it appears under him.
    async void Line(string text)
    {
        said.Text = "";
        await him.Speak(text, soFar => said.Text = soFar);
    }

    void Actions(params Border[] buttons)
    {
        actions.Children.Clear();
        foreach (var b in buttons)
        {
            b.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(b);
        }
    }

    ////////// 5. PIECES //////////

    static StackPanel Step(string title, string text, UIElement what) => new()
    {
        Children =
        {
            new TextBlock { Text = title, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Ink, TextWrapping = TextWrapping.Wrap },
            new TextBlock
            {
                Text = text,
                FontSize = 15,
                Foreground = Ink2,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                Margin = new Thickness(0, 10, 0, 18),
                MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            what,
        },
    };

    // 5. A round colour to pick, a little bigger under the pointer. The picked one has a ring, and every one in its row is
    //    redrawn on a pick.
    static Border Swatch(string colour, Func<bool> picked, Action pick, Panel row)
    {
        var grow = new ScaleTransform();
        var b = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 8, 6),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(3),
            Padding = new Thickness(3),
            Child = new Ellipse { Fill = Hex(colour), Stroke = new SolidColorBrush(Ink.Color) { Opacity = 0.11 }, StrokeThickness = 1 },
            ToolTip = colour,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = grow,
        };
        b.Tag = (Action)(() => b.BorderBrush = picked() ? Ink : Brushes.Transparent);
        ((Action)b.Tag)();
        b.MouseEnter += (_, _) => ScaleBoth(grow, new DoubleAnimation(1.08, TimeSpan.FromMilliseconds(120)));
        b.MouseLeave += (_, _) => ScaleBoth(grow, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        b.MouseLeftButtonUp += (_, _) =>
        {
            pick();
            foreach (Border s in row.Children) ((Action)s.Tag)();
        };
        return b;
    }

    // 5. One of a few, side by side, the picked one filled. Also his questions back in the chat.
    internal static WrapPanel Chips((string Value, string Name)[] options, Func<string> picked, Action<string> pick)
    {
        var row = new WrapPanel();
        void Draw()
        {
            foreach (Border c in row.Children)
            {
                bool on = (string)c.Tag == picked();
                c.Background = on ? BlueSoft : Card;
                c.BorderBrush = on ? Accent : Edge_;
                ((TextBlock)c.Child).Foreground = on ? AccentDeep : Ink2;
            }
        }
        foreach (var (value, name) in options)
        {
            var c = new Border
            {
                Tag = value,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 6, 14, 7),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    FontWeight = name.Length > 24 ? FontWeights.Normal : FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            c.MouseLeftButtonUp += (_, _) =>
            {
                pick(value);
                Draw();
            };
            row.Children.Add(c);
        }
        Draw();
        return row;
    }
}
