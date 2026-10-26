// SetupWindow.cs - setting Onsay up, the first time he opens after installing: he says hello, you pick how he looks,
// where to find him, and he is ready. Onsay stands on the left and says each step out loud, the step on the right.
//
// 1. Open(settings, done)          shows it, and done runs once it is finished or closed
// 2. Welcome, Look, Find, Ready    the four steps
// 3. LookPicker(settings, changed) his colour, face and surface, live. Also the Look page in Settings
// 4. Go(step)                      moves to a step, sliding it in, and he says its line
// 5. Swatch, Chip, Switch, Button  the pieces the steps are made of
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Onsay.Character;
using Velopack.Windows;

namespace Onsay.Desktop;

sealed class SetupWindow : Window
{
    ////////// LOOK //////////

    static readonly Brush Card = ChatWindow.Card, Page_ = ChatWindow.Page_, Edge_ = ChatWindow.Edge_, Rule = ChatWindow.Rule, Ink = ChatWindow.Ink,
        Ink2 = ChatWindow.Ink2, Ink3 = ChatWindow.Ink3, Accent = ChatWindow.Accent, AccentDeep = ChatWindow.AccentDeep, BlueSoft = ChatWindow.BlueSoft;

    // The colours he comes in: the brand's, then a few that suit most desktops.
    static readonly string[] Palette = { "#5865F2", "#3B82F6", "#12A594", "#F97316", "#E5484D", "#D946EF", "#12131C", "#FFDE59" };
    static readonly (string Value, string Name)[] Faces = { ("#FFFFFF", "Light"), ("#12131C", "Dark") };
    static readonly (string Value, string Name)[] Surfaces = { ("soft", "Soft"), ("flat", "Flat"), ("matte", "Matte"), ("glossy", "Glossy"), ("liquid", "Liquid") };

    ////////// STATE //////////

    readonly Settings settings;
    readonly Action done;
    readonly OnsayView him = new() { Width = 168, Height = 168 };
    readonly TextBlock said = new() { FontSize = 15, Foreground = Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MinHeight = 20 };
    readonly ContentControl body = new();
    readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Func<UIElement>[] steps;
    int step = -1;

    ////////// 1. OPEN //////////

    // 1.
    public static void Open(Settings settings, Action done) => new SetupWindow(settings, done).Show();

    SetupWindow(Settings settings, Action done)
    {
        this.settings = settings;
        this.done = done;
        steps = new Func<UIElement>[] { Welcome, Look, Find, Ready };
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
                                Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 3, Direction = 315, Color = Color.FromRgb(0xD9, 0xDA, 0xEC), Opacity = 1 },
                            },
                        },
                    },
                },
            },
        };
        DockPanel.SetDock(stage, System.Windows.Controls.Dock.Left);

        var close = new TextBlock
        {
            Text = ((char)0xE711).ToString(), FontFamily = ChatWindow.Icons, FontSize = 12, Foreground = Ink3, Cursor = Cursors.Hand, ToolTip = "Skip for now",
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 20, 0), Padding = new Thickness(6),
        };
        close.MouseLeftButtonUp += (_, _) => Finish();
        var bottom = new DockPanel { Margin = new Thickness(40, 0, 40, 30), Children = { actions, dots } };
        DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
        DockPanel.SetDock(bottom, System.Windows.Controls.Dock.Bottom);
        var right = new DockPanel { Children = { bottom, new Grid { Children = { new ContentControl { Content = body, Margin = new Thickness(40, 54, 40, 10) }, close } } } };

        var card = new Border
        {
            Background = Card, CornerRadius = new CornerRadius(16), BorderBrush = Edge_, BorderThickness = new Thickness(1), Margin = new Thickness(24),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 10, Direction = 270, Color = Color.FromRgb(15, 17, 21), Opacity = 0.22 },
            Child = new DockPanel { Children = { stage, right } },
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Border or Grid or DockPanel) DragMove(); };
        Content = card;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(); };
        Loaded += (_, _) => { him.React("greet"); Go(0); };
    }

    // Done or skipped: what was picked is kept, and the person meets him in his chat.
    void Finish()
    {
        if (settings.SetUp == true) return;
        settings.SetUp = true;
        settings.Look = OnsayView.Current;
        settings.Save();
        Close();
        done();
    }

    ////////// 2. THE STEPS //////////

    // 2.
    UIElement Welcome()
    {
        Line("Hi, I'm Onsay.");
        Actions(Button("Let's go", true, () => Go(1)));
        return Step("Welcome to Onsay", "A helper who lives on your computer. Tell him what you want in plain words, and he does it: on this computer, on your other ones, or on the web.",
            Note("Setting him up takes a minute. You can change all of it later in his settings."));
    }

    // 2.
    UIElement Look()
    {
        Line("How do I look?");
        Actions(Button("Back", false, () => Go(0)), Button("Next", true, () => Go(2)));
        return Step("Make him yours", "Pick his colour, his face and his surface. He changes as you pick.", LookPicker(settings, () => him.React("yes")));
    }

    // 2. Shortcuts are only for a copy that was installed, since a built one has nowhere fixed to point them.
    UIElement Find()
    {
        Line("Where should I be?");
        Actions(Button("Back", false, () => Go(1)), Button("Next", true, () => Go(3)));
        var list = new StackPanel();
        if (Updates.Installed)
        {
            list.Children.Add(Switch("A shortcut on your desktop", "Open his chat from your desktop.", Updates.Shortcut(ShortcutLocation.Desktop), on => Updates.Shortcut(ShortcutLocation.Desktop, on)));
            list.Children.Add(Switch("In the Start menu", "Find him by typing Onsay in Start.", Updates.Shortcut(ShortcutLocation.StartMenu), on => Updates.Shortcut(ShortcutLocation.StartMenu, on)));
        }
        list.Children.Add(Switch("Start with Windows", "He appears on your taskbar when you sign in.", Startup.IsOn, on => Startup.Set(on)));
        return Step("Where to find him", "He always sits on your taskbar. Click him to chat, or drag him anywhere you like.", list);
    }

    // 2.
    UIElement Ready()
    {
        Line("All set. Click me any time.");
        him.React("celebrate");
        Actions(Button("Back", false, () => Go(2)), Button("Meet Onsay", true, Finish));
        return Step("You're all set", "He's on your taskbar now. Click him to open his chat: he'll show you around and sign you in.",
            Note("Alt+Shift+O stops him at any time, from any app."));
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

    static TextBlock Label(string text) => new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 10, 0, 8) };

    static Border Note(string text) => new()
    {
        Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 11), HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = 13, Foreground = Ink3, TextWrapping = TextWrapping.Wrap },
    };

    // 5. A round colour to pick. The picked one has a ring, and every one in its row is redrawn on a pick.
    static Border Swatch(string colour, Func<bool> picked, Action pick, Panel row)
    {
        var fill = (Brush)new BrushConverter().ConvertFromString(colour)!;
        var b = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 8, 6), Cursor = Cursors.Hand, BorderThickness = new Thickness(3), Padding = new Thickness(3),
            Child = new Ellipse { Fill = fill, Stroke = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), StrokeThickness = 1 }, ToolTip = colour,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
        };
        b.Tag = (Action)(() => b.BorderBrush = picked() ? Ink : Brushes.Transparent);
        ((Action)b.Tag)();
        b.MouseEnter += (_, _) => Grow(b, 1.08);
        b.MouseLeave += (_, _) => Grow(b, 1);
        b.MouseLeftButtonUp += (_, _) => { pick(); foreach (Border s in row.Children) ((Action)s.Tag)(); };
        return b;
    }

    // 5. One of a few, side by side, the picked one filled.
    static WrapPanel Chips((string Value, string Name)[] options, Func<string> picked, Action<string> pick)
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

    // 5. A setting that is on or off: its words on the left, a switch on the right whose knob slides across.
    static Border Switch(string text, string under, bool on, Action<bool> changed)
    {
        var knob = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2), RenderTransform = new TranslateTransform() };
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center, Child = knob };
        void Draw(bool animate)
        {
            track.Background = on ? Accent : Edge_;
            var to = on ? 18.0 : 0;
            var move = (TranslateTransform)knob.RenderTransform;
            if (animate && SystemParameters.ClientAreaAnimation) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() });
            else { move.BeginAnimation(TranslateTransform.XProperty, null); move.X = to; }
        }
        DockPanel.SetDock(track, System.Windows.Controls.Dock.Right);
        var row = new Border
        {
            Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand,
            Child = new DockPanel
            {
                Children =
                {
                    track,
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink },
                            new TextBlock { Text = under, FontSize = 12.5, Foreground = Ink3, Margin = new Thickness(0, 2, 12, 0), TextWrapping = TextWrapping.Wrap },
                        },
                    },
                },
            },
        };
        row.MouseEnter += (_, _) => row.Background = Page_;
        row.MouseLeave += (_, _) => row.Background = Card;
        row.MouseLeftButtonUp += (_, _) => { on = !on; Draw(true); changed(on); };
        Draw(false);
        return row;
    }

    // 5. The main one is filled in the brand colour and sits on a hard shadow, the way the website's does.
    static Border Button(string text, bool main, Action click)
    {
        Brush rest = main ? Accent : Card, over = main ? AccentDeep : Page_;
        var b = new Border
        {
            Background = rest, BorderBrush = main ? AccentDeep : Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand,
            Padding = new Thickness(main ? 20 : 16, 9, main ? 20 : 16, 10), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
            Effect = main ? new DropShadowEffect { BlurRadius = 0, ShadowDepth = 3, Direction = 315, Color = Color.FromRgb(0x35, 0x3D, 0xB8), Opacity = 1 } : null,
            Child = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = main ? Brushes.White : Ink },
        };
        b.MouseEnter += (_, _) => b.Background = over;
        b.MouseLeave += (_, _) => { b.Background = rest; Grow(b, 1); };
        b.MouseLeftButtonDown += (_, e) => { Grow(b, 0.96); e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) => { Grow(b, 1); e.Handled = true; click(); };
        return b;
    }

    static void Grow(Border b, double to)
    {
        var s = (ScaleTransform)b.RenderTransform;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
}
