// SettingsWindow.cs - Onsay's settings, from his right click menu or the chat's: a list of pages down the left, the page
// on the right. One window at a time, above his chat, and every change takes effect as it is made.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on page when one is named
// 2. General                starting with Windows, the version, updates and removing Onsay
//    Look                   his colour, face and surface, the same picker as setting him up
// 3. Routes                 the lingos installed on this computer, each at its address under the handle, with its
//                           settings, as its manifest declares them (Fields)
// 4. Permissions            what his programs may do on this computer. A change restarts the core with the new list
// 5. Account                who is signed in, and signing in or out
// 6. Requests               every request between this computer and Onsay, newest first, each opening to the message itself
// 7. Page(...), Check(...)  the pieces every page is made of
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
        Add("Look", Look);
        Add("Routes", Routes);
        Add("Permissions", Permissions);
        Add("Account", Account);
        Add("Requests", Requests);
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

    ////////// 2. LOOK //////////

    // 2. The same picker as setting him up, with him beside it. He on the taskbar changes too.
    UIElement Look()
    {
        var p = Page("Look");
        var preview = new Character.OnsayView { Width = 96, Height = 96, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(preview, System.Windows.Controls.Dock.Left);
        p.Children.Add(new DockPanel { Children = { preview, SetupWindow.LookPicker(settings, () => preview.React("yes")) } });
        return p;
    }

    ////////// 2. GENERAL //////////

    UIElement General()
    {
        var p = Page("General");
        p.Children.Add(Check("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Startup.IsOn, on => Startup.Set(on)));

        p.Children.Add(Heading("About Onsay"));
        var version = Updates.Version;
        p.Children.Add(Paragraph(Updates.Installed ? $"Version {version}. Onsay keeps itself up to date on its own: a newer version is downloaded quietly and goes in the next time you quit."
            : "This copy was built on this computer, so it does not update itself."));
        var status = new TextBlock { FontSize = 12.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
        if (Updates.Installed)
        {
            p.Children.Add(Button("Check for updates now", async () =>
            {
                status.Text = "Checking...";
                status.Visibility = Visibility.Visible;
                status.Text = await Updates.CheckNow();
            }));
            p.Children.Add(status);
            p.Children.Add(Heading("Remove Onsay"));
            p.Children.Add(Paragraph("Takes Onsay off this computer: the app, its shortcuts and its startup. What it kept stays in your account on Onsay."));
            p.Children.Add(Button("Uninstall Onsay", () =>
            {
                Updates.Uninstall();
                Application.Current.Shutdown();
            }));
        }
        return p;
    }

    ////////// 3. ROUTES //////////

    // 3. Every lingo on this computer is a route a caller can reach at its address. Read afresh each time it's picked.
    UIElement Routes()
    {
        var p = Page("Routes");
        p.Children.Add(Paragraph("Every lingo on this computer is a route Onsay can reach when it relays a call here. " +
            "Add a lingo to the folder and it becomes a route; the core tells Onsay what it does, never its code."));
        var routes = Core.Routes(chat.Route);
        if (routes.Count == 0) p.Children.Add(Paragraph("No lingos yet. Add one to the folder below and it shows here."));
        foreach (var r in routes)
        {
            var card = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var head = new TextBlock { FontSize = 13.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new System.Windows.Documents.Run(r.Name) { FontWeight = FontWeights.SemiBold });
            if (!r.Mine) head.Inlines.Add(new System.Windows.Documents.Run("   comes with Onsay") { Foreground = Ink3, FontSize = 12 });
            card.Children.Add(head);
            card.Children.Add(new TextBlock { Text = r.Address, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            if (r.About.Length > 0) card.Children.Add(new TextBlock { Text = r.About, FontSize = 12, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var form = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            var open = Button("Settings", () =>
            {
                if (form.Children.Count == 0) Fields(form, r.Name);
                form.Visibility = form.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            });
            open.Margin = new Thickness(0, 6, 0, 0);
            card.Children.Add(open);
            card.Children.Add(form);
            p.Children.Add(card);
        }
        p.Children.Add(Button("Open the lingos folder", () => { System.IO.Directory.CreateDirectory(Core.Lingos); Native.Open(Core.Lingos); }));
        return p;
    }

    /* 3. A lingo's settings as its manifest declares them, kept on this computer wherever the lingo keeps them. A secret
          is never shown, only whether it is set, and left empty it stays as it is. Saving writes only what changed. */
    static void Fields(StackPanel form, string lingo)
    {
        var (fields, why) = Core.SettingsOf(lingo);
        if (fields is null) { form.Children.Add(Paragraph(why ?? "")); return; }
        if (fields.Count == 0) { form.Children.Add(Paragraph("This lingo has no settings.")); return; }
        var read = new List<(Core.Field field, Func<string> now)>();
        foreach (var f in fields)
        {
            form.Children.Add(new TextBlock { Text = f.Required ? $"{f.Title} (needed)" : f.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 6, 0, 2) });
            var under = f.About + (f.Default is { } d ? $" Left empty, it is {d}." : "") + (f.Secret && f.Value is not null ? " One is set: type a new one to replace it." : "");
            if (under.Trim().Length > 0) form.Children.Add(new TextBlock { Text = under.Trim(), FontSize = 11.5, Foreground = Ink3, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            var box = new Border { BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Background = Card };
            if (f.Secret) { var pw = new PasswordBox { BorderThickness = new Thickness(0), FontSize = 13 }; box.Child = pw; read.Add((f, () => pw.Password)); }
            else { var tb = new TextBox { Text = f.Value ?? "", BorderThickness = new Thickness(0), FontSize = 13 }; box.Child = tb; read.Add((f, () => tb.Text)); }
            form.Children.Add(box);
        }
        var status = new TextBlock { FontSize = 12, Foreground = Ink3, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var save = Button("Save", () =>
        {
            var changed = read.Where(x => x.field.Secret ? x.now().Length > 0 : x.now() != (x.field.Value ?? "")).ToList();
            var problem = changed.Select(x => Core.Set(lingo, x.field.Name, x.now())).FirstOrDefault(e => e is not null);
            status.Text = problem ?? (changed.Count == 0 ? "Nothing changed." : "Saved. The lingo reads them the next time it runs.");
        });
        form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { save, status } });
    }

    ////////// 4. PERMISSIONS //////////

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

    ////////// 5. ACCOUNT //////////

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

    ////////// 6. REQUESTS //////////

    // 6. Read afresh each time the page is picked, so Refresh is picking it again.
    UIElement Requests()
    {
        var p = Page("Requests");
        p.Children.Add(Paragraph("Every request between this computer and Onsay is kept here, newest first, exactly as it went, so you can see for yourself. " +
            "The prompts you send, what this computer can do, and what each lingo asks for and answers as it runs. " +
            "Your files, passwords and screen stay here unless you ask me to send something."));
        var sent = Desktop.Requests.Read();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        tools.Children.Add(Button("Refresh", () => Go(pages.FindIndex((x) => x.Name == "Requests"))));
        var file = Button("Open the log file", () => { if (System.IO.File.Exists(Desktop.Requests.File_)) Native.Open(Desktop.Requests.File_); });
        file.Margin = new Thickness(8, 0, 0, 0);
        tools.Children.Add(file);
        p.Children.Add(tools);
        if (sent.Count == 0) { p.Children.Add(Paragraph("No requests yet.")); return p; }
        p.Children.Add(Paragraph($"The newest {sent.Count} request{(sent.Count == 1 ? "" : "s")}."));
        foreach (var (at, kind, json) in sent)
        {
            var head = new TextBlock { FontSize = 12.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new System.Windows.Documents.Run($"{at:g}  ") { Foreground = Ink3, FontFamily = new FontFamily("Consolas") });
            head.Inlines.Add(new System.Windows.Documents.Run(Desktop.Requests.Kinds.GetValueOrDefault(kind, kind)));
            var body = new TextBox
            {
                Text = json.Length > 20_000 ? json[..20_000] + "\n... (the rest is in the log file)" : json, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Page_,
                FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 0),
            };
            p.Children.Add(new Expander { Header = head, Content = body, Margin = new Thickness(0, 0, 0, 6), Foreground = Ink });
        }
        return p;
    }

    ////////// 7. PIECES //////////

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
