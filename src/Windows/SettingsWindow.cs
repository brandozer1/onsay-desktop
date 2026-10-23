// SettingsWindow.cs - Onsay's settings, from his right click menu or the chat's: a list of pages down the left, the page
// on the right. One window at a time, above his chat, and every change takes effect as it is made.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on page when one is named
// 2. General                starting with Windows, debug mode, the lingos folder
// 3. Permissions            what his programs may do on this computer. A change restarts the core with the new list
// 4. Account                who is signed in, and signing in or out
// 5. Privacy                everything this computer has sent to Onsay, newest first, each opening to the message itself
// 6. Page(...), Check(...)  the pieces every page is made of
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Onsay.Desktop;

sealed class SettingsWindow : Window
{
    // The chat's colours, see ChatWindow.cs.
    static readonly Brush Card = Hex("#FFFFFF"), Page_ = Hex("#F7F7FA"), Edge_ = Hex("#D8D9E5"), Rule = Hex("#E8E8F0"),
        Ink = Hex("#12131C"), Ink2 = Hex("#4A4D5E"), Ink3 = Hex("#7B7F92"), BlueSoft = Hex("#EEF0FF");

    static SettingsWindow? open;
    readonly Settings settings;
    readonly ChatWindow chat;
    readonly StackPanel nav = new() { Margin = new Thickness(10, 14, 10, 10) };
    readonly ScrollViewer page = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(26, 22, 26, 22) };
    readonly List<(string Name, Func<UIElement> Build, Border Row)> pages = new();

    // 1.
    public static void Open(Settings settings, ChatWindow chat, string? page = null)
    {
        if (open is null) { open = new SettingsWindow(settings, chat); open.Closed += (_, _) => open = null; open.Show(); }
        if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
        if (page is not null) open.Go(open.pages.FindIndex((p) => p.Name == page));
        open.Activate();
    }

    SettingsWindow(Settings settings, ChatWindow chat)
    {
        this.settings = settings;
        this.chat = chat;
        Title = "Onsay settings";
        Width = 680;
        Height = 560;
        MinWidth = 520;
        MinHeight = 400;
        Topmost = true;   // above his chat, which is always on top
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Background = Card;

        Add("General", General);
        Add("Permissions", Permissions);
        Add("Account", Account);
        Add("Privacy", Privacy);
        var side = new Border { Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 0), Width = 180, Child = nav };
        DockPanel.SetDock(side, System.Windows.Controls.Dock.Left);
        Content = new DockPanel { Children = { side, page } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Go(0);
    }

    void Add(string name, Func<UIElement> build)
    {
        var row = new Border
        {
            Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock { Text = name, FontSize = 13.5, Foreground = Ink },
        };
        int at = pages.Count;
        row.MouseLeftButtonUp += (_, _) => Go(at);
        row.MouseEnter += (_, _) => { if (row.Tag is null) row.Background = Rule; };
        row.MouseLeave += (_, _) => { if (row.Tag is null) row.Background = Brushes.Transparent; };
        pages.Add((name, build, row));
        nav.Children.Add(row);
    }

    // Built afresh each time it's picked, so it always shows what is true now.
    void Go(int at)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            var row = pages[i].Row;
            row.Tag = i == at ? true : null;
            row.Background = i == at ? BlueSoft : Brushes.Transparent;
            ((TextBlock)row.Child).FontWeight = i == at ? FontWeights.SemiBold : FontWeights.Normal;
        }
        page.Content = pages[at].Build();
        page.ScrollToTop();
    }

    ////////// 2. GENERAL //////////

    UIElement General()
    {
        var p = Page("General");
        p.Children.Add(Check("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Startup.IsOn, on => Startup.Set(on)));
        p.Children.Add(Heading("Lingos folder"));
        p.Children.Add(Paragraph("Every lingo on this computer is a folder in here. Add or change one and it runs on this computer, and Onsay is told what it does, never its code."));
        p.Children.Add(Button("Open the lingos folder", () =>
        {
            System.IO.Directory.CreateDirectory(Core.Lingos);
            Native.Open(Core.Lingos);
        }));
        return p;
    }

    ////////// 3. PERMISSIONS //////////

    UIElement Permissions()
    {
        var p = Page("Permissions");
        p.Children.Add(Paragraph("What his programs may do on this computer, without asking. The core refuses anything not ticked, whatever a program says."));
        foreach (var (name, words, _) in Core.Permissions)
            p.Children.Add(Check(words, name, Core.Allowed(settings).Contains(name), on =>
            {
                var allow = Core.Allowed(settings);
                allow.Remove(name);
                if (on) allow.Add(name);
                settings.Allow = allow;
                settings.Save();
                chat.Permitted();
                Core.Restart();
            }));
        return p;
    }

    ////////// 4. ACCOUNT //////////

    UIElement Account()
    {
        var p = Page("Account");
        if (settings.Token is null)
        {
            p.Children.Add(Paragraph("Not signed in. His mind runs on Onsay's server, so he can't help until this computer is signed in."));
            p.Children.Add(Button("Sign in", () => { chat.SignIn(); Close(); }));
            return p;
        }
        p.Children.Add(Paragraph($"This computer is signed in to Onsay as {settings.User ?? "your account"}."));
        p.Children.Add(Button("Sign out", () => { chat.SignOut(); Go(2); }));
        return p;
    }

    ////////// 5. PRIVACY //////////

    // 5. Read afresh each time the page is picked, so Refresh is picking it again.
    UIElement Privacy()
    {
        var p = Page("Privacy");
        p.Children.Add(Paragraph("Everything this computer sends to Onsay is kept here, newest first, exactly as it went, so you can see for yourself. " +
            "What you type goes, so it can be answered, and so does what this computer can do. Of the calls a lingo makes, only their shape goes: " +
            "the keys and kinds of values that went in and came out, never a value. Your files, passwords and screen stay here unless you ask me to send something."));
        var sent = Desktop.Privacy.Read();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        tools.Children.Add(Button("Refresh", () => Go(pages.FindIndex((x) => x.Name == "Privacy"))));
        var file = Button("Open the log file", () => { if (System.IO.File.Exists(Desktop.Privacy.File_)) Native.Open(Desktop.Privacy.File_); });
        file.Margin = new Thickness(8, 0, 0, 0);
        tools.Children.Add(file);
        p.Children.Add(tools);
        if (sent.Count == 0) { p.Children.Add(Paragraph("Nothing has been sent yet.")); return p; }
        p.Children.Add(Paragraph($"The newest {sent.Count} message{(sent.Count == 1 ? "" : "s")}."));
        foreach (var (at, kind, json) in sent)
        {
            var head = new TextBlock { FontSize = 12.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new System.Windows.Documents.Run($"{at:g}  ") { Foreground = Ink3, FontFamily = new FontFamily("Consolas") });
            head.Inlines.Add(new System.Windows.Documents.Run(Desktop.Privacy.Kinds.GetValueOrDefault(kind, kind)));
            var body = new TextBox
            {
                Text = json.Length > 20_000 ? json[..20_000] + "\n... (the rest is in the log file)" : json, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Page_,
                FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0),
            };
            p.Children.Add(new Expander { Header = head, Content = body, Margin = new Thickness(0, 0, 0, 6), Foreground = Ink });
        }
        return p;
    }

    ////////// 6. PIECES //////////

    static StackPanel Page(string title) => new()
    {
        MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left,
        Children = { new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 10) } },
    };

    static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 18, 0, 2) };
    static TextBlock Paragraph(string text) => new() { Text = text, FontSize = 13, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };

    // A box to tick, with a quieter line under it.
    static UIElement Check(string text, string under, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on, FontSize = 13.5, Foreground = Ink };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 8),
            Children = { box, new TextBlock { Text = under, FontSize = 11.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 0) } },
        };
    }

    static Border Button(string text, Action click)
    {
        var b = new Border
        {
            Background = Card, BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
            Padding = new Thickness(12, 5, 12, 6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink },
        };
        b.MouseEnter += (_, _) => b.Background = Page_;
        b.MouseLeave += (_, _) => b.Background = Card;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
