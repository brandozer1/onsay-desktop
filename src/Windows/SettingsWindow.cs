// SettingsWindow.cs - Onsay's settings, from his right click menu or the chat's: a list of pages down the left, the page
// on the right. One window at a time, above his chat, and every change takes effect as it is made.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on the page named
// 2. General       starting with Windows, the version, updates and removing Onsay
// 3. Look          his colour, face and surface, the same picker as setting him up
// 4. Lingos        every lingo on this computer: on or off, what it does, whether it comes with Onsay, and its settings
//                  (Fields), or why it can't run
// 5. Permissions   the most his programs may ever do on this computer, the lingo client's ceiling
// 6. Account       signing in or out
// 7. Requests      every request between this computer and Onsay, newest first, each opening to the message itself
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Run = System.Windows.Documents.Run;
using static Onsay.Desktop.ChatWindow;

namespace Onsay.Desktop;

sealed class SettingsWindow : Window
{
    static SettingsWindow? open;
    readonly Settings settings;
    readonly ChatWindow chat;
    readonly StackPanel nav = new() { Margin = new Thickness(10, 14, 10, 10) };
    readonly ScrollViewer page = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(26, 22, 26, 22) };
    readonly List<(string Name, Func<UIElement> Build, Border Row)> pages = new();

    // 1.
    public static void Open(Settings settings, ChatWindow chat, string page = "General")
    {
        if (open is null) { open = new SettingsWindow(settings, chat); open.Closed += (_, _) => open = null; open.Show(); }
        if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
        open.Go(page);
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
        Resources.MergedDictionaries.Add(Thin);

        Add("General", General);
        Add("Look", Look);
        Add("Lingos", Lingos);
        Add("Permissions", Permissions);
        Add("Account", Account);
        Add("Requests", Requests);
        var side = new Border { Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 0), Width = 180, Child = nav };
        DockPanel.SetDock(side, System.Windows.Controls.Dock.Left);
        Content = new DockPanel { Children = { side, page } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void Add(string name, Func<UIElement> build)
    {
        var row = new Border
        {
            Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock { Text = name, FontSize = 13.5, Foreground = Ink },
        };
        row.MouseLeftButtonUp += (_, _) => Go(name);
        row.MouseEnter += (_, _) => { if (row.Tag is null) row.Background = Rule; };
        row.MouseLeave += (_, _) => { if (row.Tag is null) row.Background = Brushes.Transparent; };
        pages.Add((name, build, row));
        nav.Children.Add(row);
    }

    // Built afresh each time it's picked, so it always shows what is true now.
    void Go(string name)
    {
        foreach (var (n, build, row) in pages)
        {
            bool on = n == name;
            row.Tag = on ? true : null;
            row.Background = on ? BlueSoft : Brushes.Transparent;
            ((TextBlock)row.Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            if (on) page.Content = build();
        }
        page.ScrollToTop();
    }

    static StackPanel Page(string title) => new()
    {
        MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left,
        Children = { new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 10) } },
    };

    ////////// 2. GENERAL //////////

    UIElement General()
    {
        var p = Page("General");
        p.Children.Add(Switch("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Startup.IsOn, Startup.Set));

        p.Children.Add(Label("About Onsay"));
        if (!Updates.Installed)
        {
            p.Children.Add(Paragraph("This copy was built on this computer, so it does not update itself."));
            return p;
        }
        p.Children.Add(Paragraph($"Version {Updates.Version}. Onsay keeps itself up to date on its own: a newer version is downloaded quietly and goes in the next time you quit."));
        var status = Note("");
        status.Visibility = Visibility.Collapsed;
        p.Children.Add(Button("Check for updates now", Kind.Quiet, async () =>
        {
            status.Text = "Checking...";
            status.Visibility = Visibility.Visible;
            status.Text = await Updates.CheckNow();
        }));
        p.Children.Add(status);
        p.Children.Add(Label("Remove Onsay"));
        p.Children.Add(Paragraph("Takes Onsay off this computer: the app, its shortcuts and its startup."));
        p.Children.Add(Button("Uninstall Onsay", Kind.Danger, () =>
        {
            Updates.Uninstall();
            Application.Current.Shutdown();
        }));
        return p;
    }

    ////////// 3. LOOK //////////

    // 3. The same picker as setting him up, with him beside it. He on the taskbar changes too.
    UIElement Look()
    {
        var p = Page("Look");
        var preview = new Character.OnsayView { Width = 96, Height = 96, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(preview, System.Windows.Controls.Dock.Left);
        p.Children.Add(new DockPanel { Children = { preview, SetupWindow.LookPicker(settings, () => preview.React("yes")) } });
        return p;
    }

    ////////// 4. LINGOS //////////

    // 4. A lingo is a program on this computer. Off, nothing can use it.
    UIElement Lingos()
    {
        var p = Page("Lingos");
        p.Children.Add(Paragraph("Each lingo is a program on this computer. Turn one off and nothing can use it. To add one, put it in the lingos folder. " +
            "What you set for a lingo stays on this computer."));
        p.Children.Add(Button("Open the lingos folder", Kind.Quiet, () => Native.Open(Core.LingosFolder)));
        var lingos = Core.Lingos();
        if (lingos is null) { p.Children.Add(Paragraph("Onsay's helper on this computer isn't answering right now. Try again in a moment.")); return p; }
        if (lingos.Count == 0) p.Children.Add(Paragraph("No lingos yet."));
        foreach (var l in lingos)
        {
            var card = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            p.Children.Add(card);
            if (l.Error is not null)
            {
                card.Children.Add(Label(l.Name));
                var why = Paragraph($"It can't run because of a problem in it: {l.Error}");
                why.Foreground = Bad;
                card.Children.Add(why);
                continue;
            }
            // Its first sentence, so the list reads at a glance.
            var first = l.About.Split(". ")[0].TrimEnd('.');
            var about = $"{(first.Length > 0 ? first + "." : "")} {(l.Shipped ? "Comes with Onsay." : "")}".Trim();
            var said = Note("");
            said.Visibility = Visibility.Collapsed;
            card.Children.Add(Switch(l.Name, about.Length > 0 ? about : null, l.Running, on =>
            {
                said.Text = Core.Control(on ? "start" : "stop", new() { ["lingo"] = l.Name }).why ?? "";
                said.Visibility = said.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }));
            card.Children.Add(said);
            var form = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            card.Children.Add(Button("Settings", Kind.Quiet, () =>
            {
                if (form.Children.Count == 0) Fields(form, l.Name);
                form.Visibility = form.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            }));
            card.Children.Add(form);
        }
        return p;
    }

    /* 4. A lingo's settings as its openapi.json declares them, each with its value now, kept on this computer wherever the
          lingo keeps them. A secret is never shown, only whether it is set, and left empty it stays as it is. Saving writes
          only what changed, and an empty one goes back to its default. */
    static void Fields(StackPanel form, string lingo)
    {
        var (got, why) = Core.Control("settings", new() { ["lingo"] = lingo });
        if (got is not JsonArray fields) { form.Children.Add(Paragraph(why ?? "")); return; }
        if (fields.Count == 0) { form.Children.Add(Paragraph("This lingo has no settings.")); return; }
        var read = new List<(string name, bool secret, string? value, Func<string> now)>();
        foreach (var f in fields)
        {
            string? Text(string key) => f?[key] is { } v ? v.GetValueKind() == JsonValueKind.String ? $"{v}" : v.ToJsonString() : null;
            bool secret = (bool?)f?["secret"] == true;
            var (name, value, fallback) = (Text("name") ?? "", Text("value"), Text("default"));
            form.Children.Add(Label((Text("title") ?? name) + ((bool?)f?["required"] == true ? " (needed)" : "")));
            var under = $"{Text("about")}{(fallback is not null ? $" Left empty, it is {fallback}." : "")}{(secret && value is not null ? " One is set: type a new one to replace it." : "")}".Trim();
            if (under.Length > 0) form.Children.Add(Note(under));
            var box = new Border { BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Background = Card };
            if (secret) { var pw = new PasswordBox { BorderThickness = new Thickness(0), FontSize = 13 }; box.Child = pw; read.Add((name, true, value, () => pw.Password)); }
            else { var tb = new TextBox { Text = value ?? "", BorderThickness = new Thickness(0), FontSize = 13 }; box.Child = tb; read.Add((name, false, value, () => tb.Text)); }
            form.Children.Add(box);
        }
        var status = Note("");
        status.Margin = new Thickness(10, 0, 0, 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        var save = Button("Save", Kind.Primary, () =>
        {
            var changed = read.Where(x => x.secret ? x.now().Length > 0 : x.now() != (x.value ?? "")).ToList();
            var problem = changed.Select(x => Core.Control("set", new() { ["lingo"] = lingo, ["key"] = x.name, ["value"] = x.now().Length == 0 ? null : x.now() }).why).FirstOrDefault(e => e is not null);
            status.Text = problem ?? (changed.Count == 0 ? "Nothing changed." : "Saved. The lingo reads them the next time it runs.");
        });
        form.Children.Add(new DockPanel { Margin = new Thickness(0, 12, 0, 0), Children = { save, status } });
    }

    ////////// 5. PERMISSIONS //////////

    // 5. The ceiling: anything off, no program here can do, whatever it says. What is on, a program still asks for first.
    UIElement Permissions()
    {
        var p = Page("Permissions");
        p.Children.Add(Paragraph("What his programs may ever do on this computer. Whatever a program says, he can't do anything that is off. " +
            "For anything that matters, a program asks you first."));
        var (policy, why) = Core.Control("policy");
        if (policy?["ceiling"] is not JsonArray ceiling) { p.Children.Add(Paragraph(why ?? "")); return p; }
        var on = ceiling.Select(n => $"{n}").ToList();
        var said = Note("");
        foreach (var (name, words, _) in Core.Needs)
            p.Children.Add(Switch(words, null, on.Contains(name), yes =>
            {
                on.Remove(name);
                if (yes) on.Add(name);
                said.Text = Core.Control("ceiling", new() { ["needs"] = new JsonArray(on.Select(n => (JsonNode)n).ToArray()) }).why ?? "";
            }));
        p.Children.Add(said);
        return p;
    }

    ////////// 6. ACCOUNT //////////

    UIElement Account()
    {
        var p = Page("Account");
        if (settings.Token is null)
        {
            p.Children.Add(Paragraph("Not signed in. Sign in so he can answer you in the chat, and use the lingos on this computer for you."));
            p.Children.Add(chat.SignInBox(() => Go("Account")));
            return p;
        }
        p.Children.Add(Paragraph($"This computer is signed in to Onsay as {settings.User ?? "your account"}."));
        p.Children.Add(Button("Sign out", Kind.Quiet, () => { chat.SignOut(); Go("Account"); }));
        return p;
    }

    ////////// 7. REQUESTS //////////

    // 7. Read afresh each time the page is picked, so Refresh is picking it again.
    UIElement Requests()
    {
        var p = Page("Requests");
        p.Children.Add(Paragraph("Every request between this computer and Onsay is kept here, newest first, exactly as it went, so you can see for yourself. " +
            "The prompts you send, and what each lingo asks for and answers as it runs. " +
            "Your files, passwords and screen stay here unless you ask him to send something."));
        var sent = Desktop.Requests.Read();
        var file = Button("Open the log file", Kind.Quiet, () => { if (System.IO.File.Exists(Desktop.Requests.File_)) Native.Open(Desktop.Requests.File_); });
        file.Margin = new Thickness(8, 0, 0, 0);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { Button("Refresh", Kind.Quiet, () => Go("Requests")), file } });
        if (sent.Count == 0) { p.Children.Add(Paragraph("No requests yet.")); return p; }
        p.Children.Add(Paragraph($"The newest {sent.Count} request{(sent.Count == 1 ? "" : "s")}."));
        foreach (var (at, kind, json) in sent)
        {
            var head = new TextBlock { FontSize = 12.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new Run($"{at:g}  ") { Foreground = Ink3, FontFamily = Mono });
            head.Inlines.Add(new Run(Desktop.Requests.Kinds.GetValueOrDefault(kind, kind)));
            var body = Selectable(11.5, Ink2);
            body.Text = json.Length > 20_000 ? json[..20_000] + "\n... (the rest is in the log file)" : json;
            body.Background = Page_;
            body.FontFamily = Mono;
            body.Padding = new Thickness(8);
            body.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(new Expander { Header = head, Content = body, Margin = new Thickness(0, 0, 0, 6), Foreground = Ink });
        }
        return p;
    }
}
