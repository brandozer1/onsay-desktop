// SettingsWindow.cs - Apps and settings, from his right click menu or the chat's: a list of pages down the left, the page
// on the right. One window at a time, above his chat, and every change takes effect as it is made. Every word on it is the
// person's: a lingo is an app, an access rule is sharing, a grant is allowing.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on the page named
// 2. Apps        a list of apps, each opening to a page with tabs for what it can do, who can use it and its settings,
//                and a map of every address on this computer and what can be asked for there
//    Fields      an app's own settings, as its openapi.json declares them
// 3. Devices     the computers on the account, online or not
// 4. People      who each app is shared with, and Stop sharing
// 5. Activity    what reached this computer and what apps were allowed or refused, by day, newest first
// 6. Settings    the account, starting with Windows, his look, updates and removing Onsay
// 7. Advanced    the most any app may do, who can reach this computer, the account, folders, and the logs to read
// 8. Pieces      Section, a heading; Item, a row; Boxed, an input in its border; AppsIn, a rule's apps in words;
//                StopSharing, its button; Lines, a log's newest lines
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    readonly ScrollViewer page = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(28, 22, 28, 28),
    };
    readonly List<(string Name, Func<UIElement> Build, Border Row)> pages = new();

    // 1.
    public static void Open(Settings settings, ChatWindow chat, string page = "Apps")
    {
        if (open is null)
        {
            open = new SettingsWindow(settings, chat);
            open.Closed += (_, _) => open = null;
            open.Show();
        }
        if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
        open.Go(page);
        open.Activate();
    }

    SettingsWindow(Settings settings, ChatWindow chat)
    {
        this.settings = settings;
        this.chat = chat;
        Title = "Onsay";
        Width = 720;
        Height = 600;
        MinWidth = 560;
        MinHeight = 420;
        Topmost = true;   // above his chat, which is always on top
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Ui;
        Background = Card;
        Resources.MergedDictionaries.Add(Thin);

        Add("Apps", Apps);
        Add("Devices", Devices);
        Add("People", People);
        Add("Activity", Activity);
        Add("Settings", General);
        nav.Children.Add(new Border { Height = 1, Background = Rule, Margin = new Thickness(4, 10, 4, 10) });
        Add("Advanced", Advanced);
        var side = new Border
        {
            Background = Page_,
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Width = 150,
            Child = nav,
        };
        Content = new DockPanel { Children = { side, page } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    void Add(string name, Func<UIElement> build)
    {
        var row = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock { Text = name, FontSize = 13.5, Foreground = Ink },
        };
        row.MouseLeftButtonUp += (_, _) => Go(name);
        row.MouseEnter += (_, _) =>
        {
            if (row.Tag is null) row.Background = Rule;
        };
        row.MouseLeave += (_, _) =>
        {
            if (row.Tag is null) row.Background = Brushes.Transparent;
        };
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

    // A page: its title, with a button at its end when given, and one line on what it is for.
    static StackPanel Page(string title, string about, UIElement? end = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (end is not null) head.Children.Add(Right(end));
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var line = Paragraph(about);
        line.Margin = new Thickness(0, 0, 0, 10);
        return new StackPanel { MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left, Children = { head, line } };
    }

    const string Helper = "Onsay's helper on this computer isn't answering right now. Try again in a moment.";

    ////////// 2. APPS //////////

    const string ListView = "list", MapView = "map";
    const string CanDo = "can do", WhoCan = "who", ItsSettings = "settings";
    string appsView = ListView;   // which of the Apps page's two views shows, kept while the window is open

    // 2. Your apps or the map, the view picked. A plain list first, so nothing has to be scrolled past to reach anything.
    UIElement Apps()
    {
        var p = Page("Apps", "What Onsay can use on this computer. Turn one off and nothing can use it, not even him.");
        var lingos = Core.Lingos();
        if (lingos is null)
        {
            p.Children.Add(Paragraph(Helper));
            return p;
        }
        var views = SetupWindow.Chips(new[] { (ListView, "Your apps"), (MapView, "Map") }, () => appsView, picked =>
        {
            appsView = picked;
            Go("Apps");
        });
        views.Margin = new Thickness(0, 4, 0, 10);
        p.Children.Add(views);
        p.Children.Add(appsView == MapView ? LingoMap(lingos) : AppList(lingos));
        return p;
    }

    // 2. Each app a row: its name, what it is in a few words, and its switch. What is built into Onsay is the program
    //    itself, never an app to the person.
    UIElement AppList(List<Core.Lingo> lingos)
    {
        var list = new StackPanel();
        var apps = lingos.Where(l => !l.BuiltIn).ToList();
        if (apps.Count == 0)
        {
            list.Children.Add(Paragraph("No apps yet. To add one, open Advanced, press Open next to Apps folder, and put its file there."));
            return list;
        }
        foreach (var l in apps)
        {
            string under = l.Error is not null ? "It has a problem, so it can't run. Open it to see what."
                : l.Missing.Length > 0 ? $"Needs {string.Join(", ", l.Missing)} before it can run. Open it to set this."
                : Brief(l.About);
            FrameworkElement? onOff = l.Error is null ? Switch(l) : null;
            list.Children.Add(Item(Core.Title(l.Name), under, onOff, () => ShowApp(l, l.Missing.Length > 0 ? ItsSettings : CanDo)));
        }
        return list;
    }

    // The start of a description, so a list reads at a glance: its first sentence, cut at a colon or a comma when long.
    static string Brief(string about)
    {
        const int Most = 70;
        var first = about.Split(". ")[0].Trim().TrimEnd('.');
        if (first.Length <= Most) return first;
        int colon = first.IndexOf(':');
        if (colon >= 20) return first[..colon];
        int comma = first.LastIndexOf(", ", Most);
        return comma > 0 ? first[..comma] : first;
    }

    // An app's on and off switch. A change that fails says why.
    static Border Switch(Core.Lingo l)
    {
        return Toggle(l.Running, on =>
        {
            var problem = Core.Control(on ? "start" : "stop", new() { ["lingo"] = l.Name }).why;
            if (problem is not null) MessageBox.Show(problem, "Onsay");
        });
    }

    /* 2. One app on a page of its own: back to the list, its name, what it is and its switch, then a tab for each thing
          it has, so each fits on the screen. A change builds the tab again, so it always shows what is true. */
    void ShowApp(Core.Lingo l, string tab)
    {
        var whole = Page(Core.Title(l.Name), l.About, l.Error is null ? Switch(l) : null);
        whole.Children.Insert(0, BackLink());
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        if (l.Error is not null)
        {
            var why = Paragraph(l.Error);
            why.Foreground = Bad;
            body.Children.Add(why);
            body.Children.Add(Note("Whoever made it can fix it. Until then, nothing can use it."));
            whole.Children.Add(body);
            page.Content = whole;
            page.ScrollToTop();
            return;
        }

        void Fill()
        {
            body.Children.Clear();
            if (tab == CanDo) Abilities(body, l, Fill);
            else if (tab == WhoCan) Access(body, l, Fill);
            else Fields(body, l.Name);
        }
        var tabs = SetupWindow.Chips(new[] { (CanDo, "What it can do"), (WhoCan, "Who can use it"), (ItsSettings, "Its settings") }, () => tab, picked =>
        {
            tab = picked;
            Fill();
        });
        tabs.Margin = new Thickness(0, 6, 0, 8);
        whole.Children.Add(tabs);
        whole.Children.Add(body);
        Fill();
        page.Content = whole;
        page.ScrollToTop();
    }

    // Back to the Apps page, as a link above a page's title.
    Border BackLink()
    {
        var link = new Border
        {
            Cursor = Cursors.Hand, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock { Text = "‹ Apps", FontSize = 13, Foreground = AccentDeep },
        };
        link.MouseLeftButtonUp += (_, _) => Go("Apps");
        return link;
    }

    // The words in a list of texts in the policy, or none.
    static List<string> Texts(JsonNode? list) => (list as JsonArray)?.Select(n => $"{n}").ToList() ?? new();

    // Where a tab says what went wrong with the last change.
    static TextBlock Problem()
    {
        var said = Note("");
        said.Foreground = Bad;
        return said;
    }

    // 2. The things that matter that it says it needs, each allowed without asking or asked first, and what it was allowed
    //    for one folder.
    static void Abilities(StackPanel into, Core.Lingo l, Action again)
    {
        var (policy, why) = Core.Control("policy");
        if (policy is null)
        {
            into.Children.Add(Paragraph(why ?? Helper));
            return;
        }
        var said = Problem();
        into.Children.Add(said);
        var ceiling = Texts(policy["ceiling"]);
        var granted = Texts(policy["grants"]?[l.Name]);
        var here = Texts(policy["local"]?["overrides"]?["grants"]?[l.Name]);

        void Change(string verb, string need, bool fromHere)
        {
            var problem = Core.Control(verb, new() { ["lingo"] = l.Name, ["need"] = need }).why;
            if (problem is null) again();
            else said.Text = fromHere ? problem : "That was allowed from your account on onsay.dev, so it changes there.";
        }

        var asks = Core.Needs.Where(n => n.Name != "net" && l.Needs.Contains(n.Name)).ToList();
        foreach (var (need, words, _, _) in asks)
        {
            if (!ceiling.Contains(need))
            {
                into.Children.Add(Item(words, "Turned off for every app, under Advanced."));
                continue;
            }
            bool on = granted.Contains(need);
            string how = on ? "Allowed without asking." : need.StartsWith("files") ? "Asks you first, except in its own folder." : "Asks you first.";
            into.Children.Add(Item(words, how, Toggle(on, yes => Change(yes ? "grant" : "ungrant", need, yes || here.Contains(need)))));
        }
        foreach (var grant in granted.Where(g => g.Contains(' ')))
        {
            int space = grant.IndexOf(' ');
            string action = grant[..space] == "files.read" ? "Read" : "Change";
            into.Children.Add(Item($"{action} files in {grant[(space + 1)..]}", "Allowed without asking.", Toggle(true, _ => Change("ungrant", grant, here.Contains(grant)))));
        }

        string? reach = null;
        if (l.Needs.Contains("net")) reach = ceiling.Contains("net") ? "It can reach the internet." : "It can't reach the internet: that is turned off for every app, under Advanced.";
        else if (asks.Count == 0) reach = "It needs nothing from this computer: not your screen, your files or your programs.";
        if (reach is not null)
        {
            var line = Note(reach);
            line.Margin = new Thickness(0, 10, 0, 0);
            into.Children.Add(line);
        }
    }

    // 2. You, then each rule that lets someone in, with Stop sharing on those set here, and sharing with someone new first.
    static void Access(StackPanel into, Core.Lingo l, Action again)
    {
        var (policy, why) = Core.Control("policy");
        if (policy is null)
        {
            into.Children.Add(Paragraph(why ?? Helper));
            return;
        }
        var said = Problem();
        void Did(string? problem)
        {
            if (problem is null) again();
            else said.Text = problem;
        }

        var form = ShareForm(l, said, Did);
        form.Visibility = Visibility.Collapsed;
        into.Children.Add(Button("Share with someone", Kind.Quiet, () => form.Visibility = Shown(form.Visibility != Visibility.Visible)));
        into.Children.Add(said);
        into.Children.Add(form);

        into.Children.Add(Item("You, on this computer", "Always."));
        var local = policy["local"]?["overrides"]?["access"] as JsonArray;
        foreach (var rule in (policy["access"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var lingos = rule["lingos"];
            bool covers = lingos is JsonArray list ? list.Any(x => $"{x}" == l.Name) : $"{lingos}" is "*" || $"{lingos}" == l.Name;
            if (!covers) continue;
            var who = $"{rule["who"]}";
            bool builtIn = (bool?)rule["default"] == true;
            bool setHere = local?.Any(r => JsonNode.DeepEquals(r, rule)) == true;
            var routes = Texts(rule["routes"]).Select(r => Core.Title(r.Trim('/'))).ToList();
            string what = routes.Count > 0 ? $"Only {string.Join(", ", routes)}." : $"{lingos}" == "*" ? "Every app here." : "All of it.";
            string from = builtIn ? " Built in." : setHere ? "" : " Shared from your account on onsay.dev.";
            var stop = setHere ? Button("Stop sharing", Kind.Quiet, () => Did(Core.Control("unshare", new() { ["lingo"] = $"{lingos}", ["who"] = who }).why)) : null;
            into.Children.Add(Item(Core.Who(who, null), what + from, stop));
        }
    }

    // Sharing an app: their Onsay name, and all of it or one part, picked.
    static StackPanel ShareForm(Core.Lingo l, TextBlock said, Action<string?> did)
    {
        var form = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        var name = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
        form.Children.Add(Note("Their Onsay name, the one they signed up with, like james. They can use it from their own devices, and nothing else here."));
        form.Children.Add(Boxed(name));
        var part = "";
        if (l.Parts.Length > 1)
        {
            form.Children.Add(Label("What they can use"));
            var options = new[] { ("", "All of it") }.Concat(l.Parts.Select(x => (x, Core.Title(x.Trim('/'))))).ToArray();
            form.Children.Add(SetupWindow.Chips(options, () => part, picked => part = picked));
        }
        var go = Button("Share", Kind.Primary, () =>
        {
            var who = name.Text.Trim().ToLowerInvariant();
            if (!Regex.IsMatch(who, "^[a-z0-9_]+$") || who is "public" or "me")
            {
                said.Text = "An Onsay name is letters, digits and _, like james.";
                return;
            }
            var values = new JsonObject { ["lingo"] = l.Name, ["who"] = who };
            if (part.Length > 0) values["route"] = part;
            did(Core.Control("share", values).why);
        });
        go.Margin = new Thickness(0, 12, 0, 0);
        form.Children.Add(go);
        return form;
    }

    ////////// THE MAP //////////

    // What each group of the calls built into Onsay is, in the person's words.
    static readonly Dictionary<string, string> GroupNames = new()
    {
        ["files"] = "Files and folders", ["commands"] = "Programs and commands", ["window"] = "Windows", ["controls"] = "Buttons and boxes",
        ["input"] = "Mouse and keyboard", ["look"] = "Reading the screen", ["clipboard"] = "Clipboard", ["notify"] = "Notifications",
        ["panel"] = "Onsay's panel", ["chat"] = "Chat", ["http"] = "The web", ["store"] = "Onsay's own storage", ["compute"] = "Time and the log",
        ["std"] = "Text and lists", ["settings"] = "App settings", ["lingo"] = "Asking Onsay", ["native"] = "Advanced", ["onsay"] = "This computer on Onsay",
    };

    /* 2. Every address on this computer, as a tree that reads like a list of places: this computer, then each app at its
          address, then what can be asked of it there, in groups when it has them. Everything starts closed but the
          computer, and searching opens what matches. */
    UIElement LingoMap(List<Core.Lingo> lingos)
    {
        var (status, _) = Core.Onsay("status");
        bool named = status?["device"] is not null && status["handle"] is not null;
        string computer = named ? $"{status!["device"]}.{status["handle"]}" : "This computer";

        var map = new StackPanel();
        map.Children.Add(Note(named
            ? $"Everything that can be reached on this computer. Your other devices reach it at {computer}."
            : "Everything that can be reached on this computer. Sign in to give it a name your other devices can reach."));

        var words = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
        var hint = new TextBlock { Text = "Find something it can do", FontSize = 13, Foreground = Ink4, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 0) };
        map.Children.Add(new Border
        {
            BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 10, 0, 10),
            Child = new Grid { Children = { hint, words } },
        });

        var tree = new StackPanel();
        map.Children.Add(tree);
        void Draw()
        {
            hint.Visibility = Shown(words.Text.Length == 0);
            tree.Children.Clear();
            var ordered = lingos.OrderBy(l => l.BuiltIn).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var l in ordered) AddBranch(tree, l, computer, words.Text.Trim());
        }
        words.TextChanged += (_, _) => Draw();
        Draw();
        return map;
    }

    // One app in the map: a branch that opens to what it can do, nothing when a search finds nothing in it.
    void AddBranch(StackPanel tree, Core.Lingo l, string computer, string search)
    {
        bool Fits(Core.Call c) => search.Length == 0 || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || c.Does.Contains(search, StringComparison.OrdinalIgnoreCase);
        bool nameFits = search.Length > 0 && l.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
        var calls = l.Calls.Where(c => nameFits || Fits(c)).ToList();
        if (search.Length > 0 && calls.Count == 0) return;

        string title = l.BuiltIn ? "Built into Onsay" : Core.Title(l.Name);
        string state = l.Error is not null ? "It has a problem" : !l.Running ? "Turned off" : calls.Count == 1 ? "1 thing it can do" : $"{calls.Count} things it can do";
        Action? open = l.BuiltIn ? null : () => ShowApp(l, CanDo);
        var kids = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Visibility = Shown(search.Length > 0) };
        tree.Children.Add(Branch(title, $"{computer}{l.Route}", state, kids, 0, open));
        tree.Children.Add(kids);

        foreach (var group in calls.GroupBy(c => c.Group).OrderBy(g => g.Key))
        {
            var into = kids;
            if (group.Key.Length > 0)
            {
                into = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Visibility = Shown(search.Length > 0) };
                string name = GroupNames.GetValueOrDefault(group.Key, Core.Title(group.Key));
                string count = group.Count() == 1 ? "1 thing" : $"{group.Count()} things";
                kids.Children.Add(Branch(name, null, count, into, 1, null));
                kids.Children.Add(into);
            }
            foreach (var call in group) into.Children.Add(Leaf(call, computer));
        }
    }

    // A row of the map that opens and closes what is under it. Its Open button, when it has one, goes to the app's page.
    static Border Branch(string title, string? address, string state, StackPanel kids, int depth, Action? open)
    {
        var arrow = new TextBlock { Text = kids.Visibility == Visibility.Visible ? "▾" : "▸", FontSize = 15, Foreground = Ink2, Width = 18, VerticalAlignment = VerticalAlignment.Center };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = title, FontSize = depth == 0 ? 13.5 : 13, FontWeight = depth == 0 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ink });
        if (address is not null) words.Children.Add(new TextBlock { Text = address, FontSize = 11.5, Foreground = Ink4, TextTrimming = TextTrimming.CharacterEllipsis });

        var line = new DockPanel();
        DockPanel.SetDock(arrow, System.Windows.Controls.Dock.Left);
        line.Children.Add(arrow);
        if (open is not null)
        {
            var go = Button("Open", Kind.Quiet, open);
            go.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(go, System.Windows.Controls.Dock.Right);
            line.Children.Add(go);
        }
        var count = new TextBlock { Text = state, FontSize = 12, Foreground = Ink2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(count, System.Windows.Controls.Dock.Right);
        line.Children.Add(count);
        line.Children.Add(words);

        var row = new Border { Padding = new Thickness(6, 7, 6, 7), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = line };
        Hover(row, Page_, Brushes.Transparent);
        row.MouseLeftButtonUp += (_, _) =>
        {
            kids.Visibility = Shown(kids.Visibility != Visibility.Visible);
            arrow.Text = kids.Visibility == Visibility.Visible ? "▾" : "▸";
        };
        return row;
    }

    // One thing that can be asked for: its name, what it does, and a button that copies its address.
    static Border Leaf(Core.Call call, string computer)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = call.Name, FontSize = 13, Foreground = Ink });
        if (call.Does.Length > 0) words.Children.Add(new TextBlock { Text = call.Does, FontSize = 12, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });

        var copy = new TextBlock { Text = "Copy address", FontSize = 12, Foreground = AccentDeep, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        copy.MouseLeftButtonUp += (_, _) =>
        {
            Clipboard.SetText($"{computer}{call.Path}");
            copy.Text = "Copied";
        };
        var line = new DockPanel();
        DockPanel.SetDock(copy, System.Windows.Controls.Dock.Right);
        line.Children.Add(copy);
        line.Children.Add(words);
        return new Border { Padding = new Thickness(22, 6, 6, 6), BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1), Child = line };
    }

    /* 2. An app's settings as its openapi.json declares them, each with its value now, kept on this computer wherever the
          app keeps them. A secret is never shown, only whether it is set, and left empty it stays as it is. Saving writes
          only what changed, and an empty one goes back to its default. */
    static void Fields(StackPanel form, string lingo)
    {
        var (got, why) = Core.Control("settings", new() { ["lingo"] = lingo });
        if (got is not JsonArray fields)
        {
            form.Children.Add(Paragraph(why ?? ""));
            return;
        }
        if (fields.Count == 0)
        {
            var none = Note("This app has no settings.");
            none.Margin = new Thickness(0, 12, 0, 0);
            form.Children.Add(none);
            return;
        }
        var read = new List<(string name, bool secret, string? value, Func<string> now)>();
        foreach (var f in fields)
        {
            string? Text(string key)
            {
                var v = f?[key];
                if (v is null) return null;
                return v.GetValueKind() == JsonValueKind.String ? $"{v}" : v.ToJsonString();
            }

            bool secret = (bool?)f?["secret"] == true;
            var name = Text("name") ?? "";
            var value = Text("value");
            var fallback = Text("default");

            var title = Text("title") ?? name;
            if ((bool?)f?["required"] == true) title += " (needed)";
            form.Children.Add(Label(title));

            var under = $"{Text("about")}";
            if (fallback is not null) under += $" Left empty, it is {fallback}.";
            if (secret && value is not null) under += " One is set: type a new one to replace it.";
            under = under.Trim();
            if (under.Length > 0) form.Children.Add(Note(under));

            UIElement input;
            Func<string> now;
            if (secret)
            {
                var pw = new PasswordBox { BorderThickness = new Thickness(0), FontSize = 13 };
                input = pw;
                now = () => pw.Password;
            }
            else
            {
                var tb = new TextBox { Text = value ?? "", BorderThickness = new Thickness(0), FontSize = 13 };
                input = tb;
                now = () => tb.Text;
            }
            read.Add((name, secret, value, now));
            form.Children.Add(Boxed(input));
        }
        var status = Note("");
        status.Margin = new Thickness(10, 0, 0, 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        var save = Button("Save", Kind.Primary, () =>
        {
            var changed = read.Where(x => x.secret ? x.now().Length > 0 : x.now() != (x.value ?? "")).ToList();
            var problem = changed
                .Select(x => Core.Control("set", new() { ["lingo"] = lingo, ["key"] = x.name, ["value"] = x.now().Length == 0 ? null : x.now() }).why)
                .FirstOrDefault(e => e is not null);
            var done = changed.Count == 0 ? "Nothing changed." : "Saved. The app reads them the next time it runs.";
            status.Text = problem ?? done;
        });
        form.Children.Add(new DockPanel { Margin = new Thickness(0, 12, 0, 0), Children = { save, status } });
    }

    ////////// 3. DEVICES //////////

    // 3. As Onsay lists them, asked off this thread, since it is a trip to Onsay.
    UIElement Devices()
    {
        var p = Page("Devices", "Every computer signed in to your Onsay account. Your devices can use the apps on each other, and nobody else's.");
        var (status, why) = Core.Onsay("status");
        if (status is null)
        {
            p.Children.Add(Paragraph(why ?? Helper));
            return p;
        }
        if ((bool?)status["signed_in"] != true)
        {
            p.Children.Add(Paragraph("Sign in to see the computers on your account."));
            p.Children.Add(chat.SignInBox(() => Go("Devices")));
            return p;
        }
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { Note("Looking up your devices...") } };
        p.Children.Add(list);
        async void Load()
        {
            var (got, problem) = await Task.Run(() => Core.Onsay("devices"));
            list.Children.Clear();
            if (got is not JsonArray devices)
            {
                list.Children.Add(Paragraph(problem ?? ""));
                return;
            }
            foreach (var d in devices)
            {
                bool online = (bool?)d?["online"] == true;
                bool self = $"{d?["name"]}" == $"{status["device"]}";
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = online ? Good : Ink4,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var when = "Online now.";
                if (!online) when = DateTime.TryParse($"{d?["seen"]}", out var seen) ? $"Last seen {seen.ToLocalTime():g}." : "Not seen yet.";
                list.Children.Add(Item($"{d?["name"]}", self ? $"This computer. {when}" : when, dot));
            }
        }
        Load();
        return p;
    }

    ////////// 4. PEOPLE //////////

    // 4. Everyone an access rule names by their Onsay name, with what of each app they may use.
    UIElement People()
    {
        var p = Page("People", "People you share an app with can use it from their own devices. They can't see or use anything else on this computer.");
        var (policy, why) = Core.Control("policy");
        if (policy is null)
        {
            p.Children.Add(Paragraph(why ?? Helper));
            return p;
        }
        var local = policy["local"]?["overrides"]?["access"] as JsonArray;
        var people = (policy["access"] as JsonArray ?? new()).OfType<JsonObject>()
            .Where(r =>
            {
                var who = $"{r["who"]}";
                bool notAPerson = who is "me" or "my devices" or "public" || who.StartsWith("ip ");
                return !notAPerson && $"{r["lingos"]}" != "none";
            })
            .GroupBy(r => $"{r["who"]}");
        foreach (var person in people)
        {
            p.Children.Add(Section(person.Key));
            foreach (var rule in person)
            {
                var routes = Texts(rule["routes"]).Select(r => Core.Title(r.Trim('/'))).ToList();
                bool mine = local?.Any(r => JsonNode.DeepEquals(r, rule)) == true;
                var under = routes.Count > 0 ? $"Only {string.Join(", ", routes)}." : "All of it.";
                if (!mine) under += " Shared from your account on onsay.dev.";
                p.Children.Add(Item(AppsIn(rule), under, mine ? StopSharing(rule, "People") : null));
            }
        }
        if (!people.Any())
        {
            p.Children.Add(Paragraph("You haven't shared anything yet. To share an app, open it in Apps and press Share with someone."));
            p.Children.Add(Button("Go to Apps", Kind.Quiet, () => Go("Apps")));
        }
        return p;
    }

    ////////// 5. ACTIVITY //////////

    // Why each decision went as it did, in words, by how the lingo client says it.
    static string Because(string why) => why switch
    {
        "rule: my devices" => "Your devices can use your apps.",
        "rule: me" => "Your account can use your apps.",
        "rule: public" => "It is open to anyone.",
        "no rule lets them in" => "Nothing is shared with them.",
        "asked: once" => "You allowed it once.",
        "asked: always" => "You allowed it.",
        "asked: no" => "You said no.",
        "the ceiling" => "That is turned off for every app, under Advanced.",
        "not declared" => "The app never said it needs that.",
        "no app to ask" => "Onsay wasn't open to ask you.",
        "nobody answered" => "Nobody answered in time.",
        _ when why.StartsWith("granted: ") => "You allowed it before.",
        _ when why.StartsWith("blocked: ") => "That address is blocked, under Advanced.",
        _ when why.EndsWith(", none") => "They are kept out.",
        _ when why.StartsWith("rule: ") => "It is shared with them.",
        _ => why,
    };

    /* 5. The lingo client writes every decision but the routine to decisions.log, a line each, oldest first: each request from
          anyone but this computer, let in or not, and each thing an app was allowed or refused. The newest 300, a row
          each under its day, the time on the left. */
    UIElement Activity()
    {
        var p = Page("Activity", "What your other devices, people you share with and your apps did on this computer.", Button("Refresh", Kind.Quiet, () => Go("Activity")));
        var handle = Core.Onsay("status").answer?["handle"]?.ToString();
        var (policy, _) = Core.Control("policy");
        var local = policy?["local"]?["overrides"]?["access"] as JsonArray;
        DateTime? day = DateTime.MaxValue;
        foreach (var line in Lines(Path.Combine(Core.Home, "decisions.log"), 300))
        {
            JsonNode? d;
            try
            {
                d = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            var caller = $"{d?["caller"]}";
            var lingo = $"{d?["lingo"]}";
            var app = Core.Title(lingo);
            var need = d?["need"]?.ToString();
            bool allowed = (bool?)d?["allowed"] == true;
            var why = $"{d?["why"]}";
            var who = Core.Who(caller, handle);

            string title;
            if (need is null)
            {
                title = allowed ? $"{who} used {app}" : $"{who} tried to use {app}";
            }
            else
            {
                var name = need;
                string? path = null;
                int space = need.IndexOf(' ');
                if (space >= 0)
                {
                    name = need[..space];
                    path = need[(space + 1)..];
                }
                var words = Core.Needs.FirstOrDefault(x => x.Name == name);
                var did = words.Did ?? name;
                var wants = words.Wants ?? name;
                if (path is not null)
                {
                    did = did.Replace("your files", path);
                    wants = wants.Replace("your files", path);
                }
                title = allowed ? $"{app} {did}" : $"{app} wasn't allowed to {wants}";
                if (caller != "this computer")
                {
                    bool common = who.StartsWith("Your") || who.StartsWith("A ") || who == "Anyone";
                    var asker = common ? char.ToLower(who[0]) + who[1..] : who;
                    title += $", asked for by {asker}";
                }
            }

            DateTime? at = DateTime.TryParse($"{d?["at"]}", out var t) ? t.ToLocalTime() : null;
            if (at?.Date != day)
            {
                day = at?.Date;
                var today = DateTime.Today;
                string heading;
                if (day is not { } date) heading = "Earlier";
                else if (date == today) heading = "Today";
                else if (date == today.AddDays(-1)) heading = "Yesterday";
                else heading = $"{date:dddd d MMMM}";
                p.Children.Add(Section(heading));
            }

            var sentence = new TextBlock { FontSize = 13, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            sentence.Inlines.Add(new Run(title + ". "));
            sentence.Inlines.Add(new Run(Because(why)) { Foreground = Ink3 });
            var time = new TextBlock
            {
                Text = at?.ToString("t") ?? "",
                Width = 76,
                FontSize = 12.5,
                Foreground = Ink3,
                Margin = new Thickness(0, 1, 0, 0),
            };
            var row = new DockPanel { Children = { time } };

            // Someone's own device let in by sharing set here can be stopped from here.
            JsonObject? shared = null;
            if (caller.Split('.') is [_, var them] && them != handle && allowed && why == $"rule: {them}")
            {
                shared = local?.OfType<JsonObject>().FirstOrDefault(r => $"{r["who"]}" == them && ($"{r["lingos"]}" == lingo || $"{r["lingos"]}" == "*"));
            }
            if (shared is not null)
            {
                var stop = StopSharing(shared, "Activity");
                stop.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(Right(stop));
            }
            row.Children.Add(sentence);
            p.Children.Add(new Border
            {
                BorderBrush = Rule,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 9, 0, 9),
                Child = row,
            });
        }
        if (day == DateTime.MaxValue)
        {
            p.Children.Add(Paragraph("Nothing yet. When another of your devices or someone you share with uses an app here, or an app asks to do something that matters, it shows here."));
        }
        return p;
    }

    ////////// 6. SETTINGS //////////

    UIElement General()
    {
        var p = Page("Settings", "Your account, how he looks, and Onsay on this computer.");

        // 6. As the onsay lingo says: who this computer is signed in as, and its name on Onsay.
        p.Children.Add(Section("Your account"));
        var (status, why) = Core.Onsay("status");
        if (status is null)
        {
            p.Children.Add(Paragraph(why ?? Helper));
        }
        else if ((bool?)status["signed_in"] != true)
        {
            p.Children.Add(Paragraph("Not signed in. Sign in so he can answer you in the chat, and so your other devices can reach this one."));
            p.Children.Add(chat.SignInBox(() => Go("Settings")));
        }
        else
        {
            var handle = status["handle"];
            var device = status["device"];
            var signedIn = handle is null ? "Signed in" : $"Signed in as {handle}";
            var thisComputer = device is null ? "It connects when Onsay is on." : $"This computer is {device}.";
            var said = Note("");
            var signOut = Button("Sign out", Kind.Quiet, () =>
            {
                var problem = chat.SignOut();
                if (problem is not null) said.Text = problem;
                else Go("Settings");
            });
            p.Children.Add(Item(signedIn, thisComputer, signOut));
            p.Children.Add(said);
        }

        p.Children.Add(Section("On this computer"));
        p.Children.Add(Item("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Toggle(Startup.IsOn, Startup.Set)));

        // 6. The same picker as setting him up, with him beside it. He on the taskbar changes too.
        p.Children.Add(Section("His look"));
        var preview = new Character.OnsayView
        {
            Width = 80,
            Height = 80,
            Margin = new Thickness(0, 12, 28, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        p.Children.Add(new DockPanel { Children = { preview, SetupWindow.LookPicker(settings, () => preview.React("yes")) } });

        p.Children.Add(Section("Updates"));
        if (!Updates.Installed)
        {
            p.Children.Add(Item("Built on this computer", "This copy doesn't update itself."));
            return p;
        }
        var checking = Note("");
        p.Children.Add(Item($"Version {Updates.Version}", "A newer version downloads quietly and goes in the next time you quit.", Button("Check now", Kind.Quiet, async () =>
        {
            checking.Text = "Checking...";
            checking.Text = await Updates.CheckNow();
        })));
        p.Children.Add(checking);
        p.Children.Add(Section("Remove Onsay"));
        p.Children.Add(Item("Uninstall Onsay", "Takes the app, its shortcuts and its startup off this computer, and asks whether your settings and chats go too.", Button("Uninstall", Kind.Danger, () =>
        {
            Updates.Uninstall();
            Application.Current.Shutdown();
        })));
        return p;
    }

    ////////// 7. ADVANCED //////////

    // What each kind of message sent to Onsay is, in plain words.
    static readonly Dictionary<string, string> Kinds = new()
    {
        ["hello"] = "This computer connected to Onsay, with the apps on it",
        ["catalog"] = "The apps on this computer changed",
        ["prompt"] = "Something you asked, with the chat before it",
        ["lingo"] = "An app asked Onsay for something while it ran",
        ["response"] = "An app's answer to a request that came through Onsay",
        ["applied"] = "Your account's newest rules, as this computer put them in place, and any it wouldn't allow",
        ["log"] = "What was allowed or refused on this computer, for your Activity",
        ["api"] = "A question to Onsay: signing in, your account's rules, your devices, or checking another of your devices",
    };

    /* 7. Everything underneath, a row each in plain labels: the ceiling, the access rules and blocks in force, the account's
          rules, this device's key and the prompt key, the folders, and the logs, each read in place: sent.log, which the
          onsay lingo keeps in the lingo client's home, decisions.log beside it, and the lingo client's own core.log. */
    UIElement Advanced()
    {
        var p = Page("Advanced", "How Onsay works underneath, for anyone who wants to look. Nothing here needs changing for him to work.");
        var (policy, why) = Core.Control("policy");
        var status = Core.Onsay("status").answer;

        void Hint(string text)
        {
            var n = Note(text);
            n.Margin = new Thickness(0, 0, 0, 4);
            n.Foreground = Ink2;
            p.Children.Add(n);
        }

        // A row with View, which opens the text read under it, newest first, with Copy and Open the file.
        void Log(string title, string under, Func<string> read, string? file = null)
        {
            var box = new Border
            {
                Visibility = Visibility.Collapsed,
                Background = Page_,
                BorderBrush = Rule,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 6, 0, 10),
            };
            Border view = null!;
            view = Button("View", Kind.Quiet, () =>
            {
                bool showing = box.Visibility != Visibility.Visible;
                box.Visibility = Shown(showing);
                ((TextBlock)view.Child).Text = showing ? "Hide" : "View";
                if (!showing) return;

                var got = read();
                var text = Selectable(11.5, Ink2);
                text.FontFamily = Mono;
                text.Text = got.Length > 0 ? got : "Nothing yet.";
                var copy = Button("Copy", Kind.Quiet, () => Clipboard.SetText(text.Text));
                var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 12), Children = { copy } };
                if (file is not null && File.Exists(file))
                {
                    var openFile = Button("Open the file", Kind.Quiet, () => Native.Open(file));
                    openFile.Margin = new Thickness(8, 0, 0, 0);
                    bar.Children.Add(openFile);
                }
                var scroll = new ScrollViewer
                {
                    MaxHeight = 320,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(12, 10, 12, 10),
                    Content = text,
                };
                box.Child = new StackPanel { Children = { scroll, bar } };
            });
            p.Children.Add(Item(title, under, view));
            p.Children.Add(box);
        }

        p.Children.Add(Section("What any app may do here"));
        Hint("Anything off, no app here can do, whatever it says or is allowed. Anything on, an app still asks you first.");
        if (policy?["ceiling"] is JsonArray ceiling)
        {
            var on = ceiling.Select(n => $"{n}").ToList();
            var said = Problem();
            foreach (var (name, words, _, _) in Core.Needs)
            {
                var toggle = Toggle(on.Contains(name), yes =>
                {
                    on.Remove(name);
                    if (yes) on.Add(name);
                    var needs = new JsonArray(on.Select(n => (JsonNode)n).ToArray());
                    said.Text = Core.Control("ceiling", new() { ["needs"] = needs }).why ?? "";
                });
                p.Children.Add(Item(words, null, toggle));
            }
            p.Children.Add(said);
        }
        else
        {
            p.Children.Add(Paragraph(why ?? Helper));
        }

        p.Children.Add(Section("Who can reach this computer"));
        Hint("Anyone not listed gets nothing, as if this computer weren't there. You, on this computer, always can.");
        var local = policy?["local"]?["overrides"]?["access"] as JsonArray;
        foreach (var rule in (policy?["access"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var what = AppsIn(rule);
            var routes = Texts(rule["routes"]).Select(r => Core.Title(r.Trim('/'))).ToList();
            if (routes.Count > 0) what += $", only {string.Join(", ", routes)}";
            if ($"{rule["lingos"]}" == "onsay") what += ", with your chat key";

            string from;
            if ((bool?)rule["default"] == true) from = "Built in.";
            else if (local?.Any(x => JsonNode.DeepEquals(x, rule)) == true) from = "Set on this computer.";
            else from = "From your account.";

            p.Children.Add(Item(Core.Who($"{rule["who"]}", null), $"{what}. {from}"));
        }
        foreach (var blocked in policy?["block"] as JsonArray ?? new())
        {
            p.Children.Add(Item($"A computer at {blocked}", "Blocked from everything."));
        }
        var rules = Path.Combine(Core.Home, "policy.json");
        if (File.Exists(rules))
        {
            p.Children.Add(Item("This computer's own rules", "Kept in a file in Onsay's folder.", Button("Open", Kind.Quiet, () => Native.Open(rules))));
        }
        if (policy is not null)
        {
            var version = policy["account"]?["version"];
            var inForce = version is null
                ? "Your account has no rules yet, so this computer uses its own."
                : $"Your account's rules are version {version}. This computer's own come first.";
            Log("Every rule in force here", inForce, () => policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        p.Children.Add(Section("Keys"));
        var copied = Note("");
        var key = status?["key"];
        var keyIs = key is null ? "It gets one when it signs in." : "Proves a request really came from here. It never leaves this computer.";
        Border? copyKey = null;
        if (key is not null)
        {
            copyKey = Button("Copy", Kind.Quiet, () =>
            {
                Clipboard.SetText($"{key}");
                copied.Text = "Copied this computer's key.";
            });
        }
        p.Children.Add(Item("This computer's key", keyIs, copyKey));
        p.Children.Add(Item("Chat key", "Lets another program send him a message. Keep it private.", Button("Copy", Kind.Quiet, () =>
        {
            var own = Core.Control("settings", new() { ["lingo"] = "onsay" }).answer?.AsArray();
            var value = own?.FirstOrDefault(s => $"{s?["name"]}" == "prompt_key")?["value"];
            if (value is null)
            {
                copied.Text = "There's no chat key yet. Onsay makes one the first time it is on.";
                return;
            }
            Clipboard.SetText($"{value}");
            copied.Text = $"Copied the chat key. A program sends him a message at {Core.Url}/onsay with it.";
        })));
        p.Children.Add(copied);

        p.Children.Add(Section("Folders"));
        p.Children.Add(Item("Onsay's folder", "Everything he keeps on this computer.", Button("Open", Kind.Quiet, () => Native.Open(Settings.Folder))));
        p.Children.Add(Item("Apps folder", "To add an app, put its file here. It shows in Apps, off until you turn it on.", Button("Open", Kind.Quiet, () => Native.Open(Core.LingosFolder))));
        p.Children.Add(Item("The program that runs your apps", $"It answers at {Core.Url}, for this computer alone.", Button("Open its settings", Kind.Quiet, () => Native.Open(Core.Config))));

        p.Children.Add(Section("Logs"));
        Hint("Newest first, exactly as written.");

        // One line of sent.log in words, or as it is when it can't be read.
        string Sent(string line)
        {
            JsonNode? s;
            try
            {
                s = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                return line;
            }
            var json = s?["message"]?.ToJsonString() ?? "";
            var shown = json.Length > 4000 ? json[..4000] + "\n... (the rest is in the file)" : json;
            var kind = $"{s?["message"]?["type"]}";
            DateTime.TryParse($"{s?["at"]}", out var at);
            return $"{at:g}  {Kinds.GetValueOrDefault(kind, kind)}\n{shown}";
        }

        var sent = Path.Combine(Core.Home, "sent.log");
        Log("Everything sent to Onsay", "What you ask, and what each app asks for and answers.", () => string.Join("\n\n", Lines(sent, 300).Select(Sent)), sent);
        var decisions = Path.Combine(Core.Home, "decisions.log");
        Log("Activity, as written", "Every request and every allow or refuse, the way the program writes it down.", () => string.Join("\n", Lines(decisions, 500)), decisions);
        Log("The program's log", "What the program that runs your apps says while it works, and any errors.", () => string.Join("\n", Lines(Core.Log, 500)), Core.Log);
        return p;
    }

    ////////// 8. PIECES //////////

    // 8. A heading over a few rows.
    static TextBlock Section(string text)
    {
        var heading = Label(text);
        heading.FontSize = 15;
        heading.Margin = new Thickness(0, 24, 0, 2);
        return heading;
    }

    /* 8. A row: a title, a quieter line under it, cut short past two lines, and a button, a switch or a dot at its
          end. With click, the whole row is a button that lights up under the pointer. */
    static Border Item(string title, string? under, FrameworkElement? end = null, Action? click = null)
    {
        var words = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, FontSize = 13.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap } },
        };
        if (under is not null)
        {
            words.Children.Add(new TextBlock
            {
                Text = under,
                FontSize = 12,
                Foreground = Ink2,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 34,
                TextTrimming = TextTrimming.WordEllipsis,
                ToolTip = under,
            });
        }
        var row = new DockPanel();
        if (end is not null)
        {
            end.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(Right(end));
        }
        row.Children.Add(words);
        var b = new Border
        {
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 10, 8, 10),
            Margin = new Thickness(-8, 0, -8, 0),
            Background = Brushes.Transparent,
            Child = row,
        };
        if (click is null) return b;
        b.Cursor = Cursors.Hand;
        Hover(b, Page_, Brushes.Transparent);
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    // 8. A text box or password box in its border. Also setting him up's key field.
    internal static Border Boxed(UIElement input) => new()
    {
        BorderBrush = Edge_,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(6, 4, 6, 4),
        Background = Card,
        Child = input,
    };

    // 8. The apps an access rule covers, in words.
    static string AppsIn(JsonObject rule)
    {
        if (rule["lingos"] is JsonArray list) return string.Join(", ", list.Select(x => Core.Title($"{x}")));
        var one = $"{rule["lingos"]}";
        if (one == "*") return "Every app";
        if (one == "none") return "Nothing: kept out";
        return Core.Title(one);
    }

    // 8. Ends an access rule set on this computer, then shows the page named again.
    Border StopSharing(JsonObject rule, string page) => Button("Stop sharing", Kind.Quiet, () =>
    {
        Core.Control("unshare", new() { ["lingo"] = $"{rule["lingos"]}", ["who"] = $"{rule["who"]}" });
        Go(page);
    });

    // 8. A log's newest lines, newest first, read while the program may still be writing it. None when there is no file yet.
    static IEnumerable<string> Lines(string path, int newest)
    {
        var lines = new List<string>();
        try
        {
            using var read = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (string? line; (line = read.ReadLine()) is not null;) lines.Add(line);
        }
        catch (IOException) { }
        return Enumerable.Reverse(lines).Take(newest);
    }
}
