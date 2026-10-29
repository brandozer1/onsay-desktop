// SetupWindow.cs - setting Onsay up, the first time he opens after installing: he says hello, you pick how he looks and
// where to find him, read what is sent to Onsay, and sign in. Onsay stands on the left and says each step out loud, the
// step on the right.
//
// 1. Open(settings, chat, done)    shows it, and done runs once it is finished or skipped
// 2. Welcome, Look, Find, Sent, SignIn   the steps
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
using Velopack.Windows;
using static Onsay.Desktop.ChatWindow;

namespace Onsay.Desktop;

sealed class SetupWindow : Window
{
    ////////// LOOK //////////

    // The colours he comes in: the brand's, then a few that suit most desktops. His surfaces are OnsayView's.
    static readonly string[] Palette = { Brand, "#3B82F6", "#12A594", "#F97316", "#E5484D", "#D946EF", "#12131C", "#FFDE59" };
    static readonly (string Value, string Name)[] Faces = { ("#FFFFFF", "Light"), ("#12131C", "Dark") };
    static readonly (string Value, string Name)[] Surfaces = OnsayView.Materials.Keys.Select(k => (k, char.ToUpper(k[0]) + k[1..])).ToArray();

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
        steps = new Func<UIElement>[] { Welcome, Look, Find, Sent, SignIn };
        Title = "Set up Onsay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Width = 820;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Topmost = true;

        // The stage: Onsay on a dotted floor, with what he says under him.
        var dot = new DrawingBrush(new GeometryDrawing(Edge_, null, new EllipseGeometry(new Point(1, 1), 1, 1)))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 22, 22), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 22, 22), ViewboxUnits = BrushMappingMode.Absolute,
        };
        var stage = new Border
        {
            Width = 320, Background = Page_, CornerRadius = new CornerRadius(16, 0, 0, 16), BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid
            {
                Children =
                {
                    new Border { Background = dot, CornerRadius = new CornerRadius(16, 0, 0, 16), Opacity = 0.9 },
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 0),
                        Children =
                        {
                            him,
                            new Border
                            {
                                Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12),
                                Margin = new Thickness(0, 22, 0, 0), Child = said,
                                Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 3, Direction = 315, Color = DropSoft.Color, Opacity = 1 },
                            },
                        },
                    },
                },
            },
        };
        DockPanel.SetDock(stage, System.Windows.Controls.Dock.Left);

        var close = Button("", Kind.Icon, Skip, "Skip for now");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 14, 16, 0);
        var bottom = new DockPanel { Margin = new Thickness(40, 0, 40, 30), Children = { actions, dots } };
        DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
        DockPanel.SetDock(bottom, System.Windows.Controls.Dock.Bottom);
        var right = new DockPanel { Children = { bottom, new Grid { Children = { new ContentControl { Content = body, Margin = new Thickness(40, 54, 40, 10) }, close } } } };

        var card = new Border
        {
            Background = Card, CornerRadius = new CornerRadius(16), BorderBrush = Edge_, BorderThickness = new Thickness(1), Margin = new Thickness(24),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 10, Direction = 270, Opacity = 0.22 },
            Child = new DockPanel { Children = { stage, right } },
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Border or Grid or DockPanel) DragMove(); };
        Content = card;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Skip(); };
        Loaded += (_, _) => { him.React("greet"); Go(0); };
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
            list.Children.Add(Switch("A shortcut on your desktop", "Open his chat from your desktop.", Updates.Shortcut(ShortcutLocation.Desktop), on => Updates.Shortcut(ShortcutLocation.Desktop, on)));
            list.Children.Add(Switch("In the Start menu", "Find him by typing Onsay in Start.", Updates.Shortcut(ShortcutLocation.StartMenu), on => Updates.Shortcut(ShortcutLocation.StartMenu, on)));
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

    // 2. Signed in, here or before, it is the last word: all set.
    UIElement SignIn()
    {
        if (Core.Onsay("status").answer is { } status && (bool?)status["signed_in"] == true)
        {
            Line("All set. Click me any time.");
            him.React("celebrate");
            Actions(Button("Meet Onsay", Kind.Primary, Finish));
            return Step("You're all set", $"{(status["handle"] is { } handle ? $"You're {handle}. " : "You're signed in. ")}He's on your taskbar now: click him any time to open his chat.", new Border());
        }
        Line("Let's sign you in.");
        Actions(Button("Back", Kind.Quiet, () => Go(3)), Button("Skip for now", Kind.Quiet, Finish));
        return Step("Sign in", "Signing in gives this computer its own name on Onsay, like laptop.yourname, so he can answer you in the chat.",
            chat.SignInBox(() => Go(4)));
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
        foreach (var c in Palette) colours.Children.Add(Swatch(c, () => look.Color.Equals(c, StringComparison.OrdinalIgnoreCase), () => Set(look = look with { Color = c }), colours));
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
        if (content is FrameworkElement f) { f.RenderTransform = slide; f.Opacity = 0; }
        body.Content = content;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 320 : 1);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, time) { EasingFunction = ease });
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, time) { EasingFunction = ease });

        dots.Children.Clear();
        for (int i = 0; i < steps.Length; i++)
            dots.Children.Add(new Border { Width = i == to ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), Background = i == to ? Accent : i < to ? BlueSoft : Rule });
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
        foreach (var b in buttons) { b.Margin = new Thickness(10, 0, 0, 0); actions.Children.Add(b); }
    }

    ////////// 5. PIECES //////////

    static StackPanel Step(string title, string text, UIElement what) => new()
    {
        Children =
        {
            new TextBlock { Text = title, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Ink, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = text, FontSize = 15, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, LineHeight = 23, Margin = new Thickness(0, 10, 0, 18), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left },
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
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 8, 6), Cursor = Cursors.Hand, BorderThickness = new Thickness(3), Padding = new Thickness(3),
            Child = new Ellipse { Fill = Hex(colour), Stroke = new SolidColorBrush(Ink.Color) { Opacity = 0.11 }, StrokeThickness = 1 }, ToolTip = colour,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = grow,
        };
        void Grow(double to)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120));
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        b.Tag = (Action)(() => b.BorderBrush = picked() ? Ink : Brushes.Transparent);
        ((Action)b.Tag)();
        b.MouseEnter += (_, _) => Grow(1.08);
        b.MouseLeave += (_, _) => Grow(1);
        b.MouseLeftButtonUp += (_, _) => { pick(); foreach (Border s in row.Children) ((Action)s.Tag)(); };
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
                Tag = value, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 6, 14, 7), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold },
            };
            c.MouseLeftButtonUp += (_, _) => { pick(value); Draw(); };
            row.Children.Add(c);
        }
        Draw();
        return row;
    }
}
