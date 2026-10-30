// SettingsWindow.cs - Apps and settings, from his right click menu or the chat's: a list of pages down the left, the page
// on the right. One window at a time, above his chat, and every change takes effect as it is made. Every word on it is the
// person's: a lingo is an app, an access rule is sharing, a grant is allowing.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on the page named
// 2. Apps        a row for each app with its switch, the helpers folded under one row. A row opens what it can do, who
//                can use it, Share and its own settings
//    Fields      an app's own settings, as its openapi.json declares them
// 3. Devices     the computers on the account, online or not
// 4. People      who each app is shared with, and Stop sharing
// 5. Activity    what reached this computer and what apps were allowed or refused, by day, newest first
// 6. Settings    the account, starting with Windows, his look, updates and removing Onsay
// 7. Advanced    the most any app may do, who can reach this computer, the account, folders, and the logs to read
// 8. Pieces      Section, a heading; Item, a row; Lines, a log's lines
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
    readonly ScrollViewer page = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 22, 28, 28) };
    readonly List<(string Name, Func<UIElement> Build, Border Row)> pages = new();

    // 1.
    public static void Open(Settings settings, ChatWindow chat, string page = "Apps")
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
        Title = "Onsay";
        Width = 720;
        Height = 600;
        MinWidth = 560;
        MinHeight = 420;
        Topmost = true;   // above his chat, which is always on top
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Background = Card;
        Resources.MergedDictionaries.Add(Thin);

        Add("Apps", Apps);
        Add("Devices", Devices);
        Add("People", People);
        Add("Activity", Activity);
        Add("Settings", General);
        nav.Children.Add(new Border { Height = 1, Background = Rule, Margin = new Thickness(4, 10, 4, 10) });
        Add("Advanced", Advanced);
        var side = new Border { Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 0), Width = 150, Child = nav };
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

    // A page: its title, with a button at its end when given, and one line on what it is for.
    static StackPanel Page(string title, string about, UIElement? end = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (end is not null) { DockPanel.SetDock(end, System.Windows.Controls.Dock.Right); head.Children.Add(end); }
        head.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        var line = Paragraph(about);
        line.Margin = new Thickness(0, 0, 0, 10);
        return new StackPanel { MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left, Children = { head, line } };
    }

    const string Helper = "Onsay's helper on this computer isn't answering right now. Try again in a moment.";

    ////////// 2. APPS //////////

    // The ones Onsay leads with, then the rest by name, and the small tools other apps build on, folded under Helpers.
    static readonly string[] Lead = { "windows", "agent", "onsay", "native" };
    static readonly string[] Helpers = { "encode", "json", "list", "math", "request", "text", "time", "url" };

    // 2. Each app is a row: its name, the start of what it is, and its switch. A click on the row opens the rest under it.
    UIElement Apps()
    {
        var p = Page("Apps", "What Onsay can use on this computer. Turn one off and nothing can use it, not even him.");
        var lingos = Core.Lingos();
        if (lingos is null) { p.Children.Add(Paragraph(Helper)); return p; }
        if (lingos.Count == 0) { p.Children.Add(Paragraph("No apps yet. To add one, open Advanced, press Open next to Apps folder, and put its file there.")); return p; }
        var main = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var helpers = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 0, 0) };
        var rows = new List<(Core.Lingo l, Border row, Border more)>();
        foreach (var l in lingos.OrderBy(l => Array.IndexOf(Lead, l.Name) is var i and >= 0 ? i : Lead.Length).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var into = new StackPanel();
            var more = new Border { Visibility = Visibility.Collapsed, Background = Page_, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 2, 16, 14), Margin = new Thickness(0, 6, 0, 10), Child = into };
            void Open(Action fill)
            {
                more.Visibility = Shown(more.Visibility != Visibility.Visible);
                if (more.Visibility == Visibility.Visible) fill();
            }
            var said = Note("");
            said.Margin = new Thickness(0, 6, 0, 4);
            Border row;
            if (l.Error is not null)
                row = Item(Core.Title(l.Name), "It can't run because of a problem in it. Open it to see what.", null, () => Open(() =>
                {
                    into.Children.Clear();
                    var why = Paragraph(l.Error);
                    why.Foreground = Bad;
                    into.Children.Add(why);
                    into.Children.Add(Note("Whoever made it can fix it. Until then, nothing can use it."));
                }));
            else
            {
                // The start of its first sentence, so the list reads at a glance: up to its colon, or a comma that fits.
                var first = l.About.Split(". ")[0].Trim().TrimEnd('.');
                if (first.Length > 70) first = first.IndexOf(':') is var colon and >= 20 ? first[..colon] : first.LastIndexOf(", ", 70) is var comma and > 0 ? first[..comma] : first;
                // One built into Onsay is always on, so it has no switch.
                var toggle = l.BuiltIn ? null : Toggle(l.Running, on => { said.Text = Core.Control(on ? "start" : "stop", new() { ["lingo"] = l.Name }).why ?? ""; });
                row = Item(Core.Title(l.Name), first.Length > 0 ? first : null, toggle, () => Open(() => Details(into, l)));
            }
            var list = Helpers.Contains(l.Name) ? helpers : main;
            list.Children.Add(row);
            list.Children.Add(said);
            list.Children.Add(more);
            rows.Add((l, row, more));
        }
        var tools = lingos.Where(l => Helpers.Contains(l.Name)).Select(l => Core.Title(l.Name)).ToList();
        if (tools.Count > 0)
        {
            var chevron = new TextBlock { Text = "", FontFamily = Icons, FontSize = 12, Foreground = Ink3, VerticalAlignment = VerticalAlignment.Center };
            main.Children.Add(Item("Helpers", $"Small tools other apps use: {string.Join(", ", tools.Take(3))}{(tools.Count > 3 ? $" and {tools.Count - 3} more" : "")}", chevron, () =>
            {
                helpers.Visibility = Shown(helpers.Visibility != Visibility.Visible);
                chevron.Text = helpers.Visibility == Visibility.Visible ? "" : "";
            }));
            main.Children.Add(helpers);
        }
        // A search box once the list is long enough to need one.
        if (lingos.Count - tools.Count > 8)
        {
            var box = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
            var hint = new TextBlock { Text = "Search apps", FontSize = 13, Foreground = Ink4, IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 0) };
            box.TextChanged += (_, _) =>
            {
                hint.Visibility = Shown(box.Text.Length == 0);
                if (box.Text.Length > 0) helpers.Visibility = Visibility.Visible;
                foreach (var (l, row, more) in rows)
                {
                    row.Visibility = Shown(l.Name.Contains(box.Text, StringComparison.OrdinalIgnoreCase) || l.About.Contains(box.Text, StringComparison.OrdinalIgnoreCase));
                    if (row.Visibility != Visibility.Visible) more.Visibility = Visibility.Collapsed;
                }
            };
            p.Children.Add(new Border
            {
                BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 8, 0, 0),
                Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Child = new Grid { Children = { hint, box } },
            });
        }
        p.Children.Add(main);
        return p;
    }

    /* 2. An app opened: what it can do (each thing that matters it says it needs, allowed without
          asking or asked first, and what it was allowed for one folder), who can use it (you, then each rule that lets
          someone in, with Stop sharing on those set here), Share and its own settings. A change builds it again, so it
          always shows what is true. */
    void Details(StackPanel into, Core.Lingo l)
    {
        into.Children.Clear();
        var (policy, why) = Core.Control("policy");
        if (policy is null) { into.Children.Add(Paragraph(why ?? Helper)); return; }
        var said = Note("");
        said.Foreground = Bad;
        void Did(string? problem) { if (problem is null) Details(into, l); else said.Text = problem; }
        static List<string> Texts(JsonNode? list) => (list as JsonArray)?.Select(n => $"{n}").ToList() ?? new();
        var ceiling = Texts(policy["ceiling"]);
        var granted = Texts(policy["grants"]?[l.Name]);
        var here = Texts(policy["local"]?["overrides"]?["grants"]?[l.Name]);

        into.Children.Add(Label("What it can do"));
        var asks = Core.Needs.Where(n => n.Name != "net" && l.Needs.Contains(n.Name)).ToList();
        foreach (var (need, words, _, _) in asks)
        {
            if (!ceiling.Contains(need)) { into.Children.Add(Item(words, "Turned off for every app, under Advanced.")); continue; }
            bool on = granted.Contains(need);
            into.Children.Add(Item(words, on ? "Allowed without asking." : need.StartsWith("files") ? "Asks you first, except in its own folder." : "Asks you first.", Toggle(on, yes =>
                Did(Core.Control(yes ? "grant" : "ungrant", new() { ["lingo"] = l.Name, ["need"] = need }).why is { } problem
                    ? here.Contains(need) || yes ? problem : "That was allowed from your account on onsay.dev, so it changes there." : null))));
        }
        foreach (var g in granted.Where(g => g.Contains(' ')))
        {
            var (need, path) = (g[..g.IndexOf(' ')], g[(g.IndexOf(' ') + 1)..]);
            into.Children.Add(Item($"{(need == "files.read" ? "Read" : "Change")} files in {path}", "Allowed without asking.", Toggle(true, _ =>
                Did(Core.Control("ungrant", new() { ["lingo"] = l.Name, ["need"] = g }).why is { } problem
                    ? here.Contains(g) ? problem : "That was allowed from your account on onsay.dev, so it changes there." : null))));
        }
        var reach = l.Needs.Contains("net") ? ceiling.Contains("net") ? "It can reach the internet." : "It can't reach the internet: that is turned off for every app, under Advanced."
            : asks.Count == 0 ? "It needs nothing from this computer: not your screen, your files or your programs." : null;
        if (reach is not null) { var n = Note(reach); n.Margin = new Thickness(0, 8, 0, 0); into.Children.Add(n); }

        into.Children.Add(Label("Who can use it"));
        into.Children.Add(Item("You, on this computer", "Always."));
        var local = policy["local"]?["overrides"]?["access"] as JsonArray;
        foreach (var rule in (policy["access"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var lingos = rule["lingos"];
            if (!(lingos is JsonArray list ? list.Any(x => $"{x}" == l.Name) : $"{lingos}" is "*" || $"{lingos}" == l.Name)) continue;
            var who = $"{rule["who"]}";
            bool built = (bool?)rule["default"] == true, mine = local?.Any(r => JsonNode.DeepEquals(r, rule)) == true;
            var routes = Texts(rule["routes"]).Select(r => Core.Title(r.Trim('/'))).ToList();
            var what = routes.Count > 0 ? $"Only {string.Join(", ", routes)}." : $"{lingos}" == "*" ? "Every app here." : "All of it.";
            var title = l.Name == "onsay" && who == "public" && built ? "Other programs, with your chat key" : Core.Who(who, null);
            var stop = mine ? Button("Stop sharing", Kind.Quiet, () => Did(Core.Control("unshare", new() { ["lingo"] = $"{lingos}", ["who"] = who }).why)) : null;
            into.Children.Add(Item(title, $"{what}{(built ? " Built in." : mine ? "" : " Shared from your account on onsay.dev.")}", stop));
        }

        // Share: their Onsay name, and all of it or one part, picked. Its settings: as the app declares them.
        var form = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
        var fields = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var name = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
        buttons.Children.Add(Button("Share with someone", Kind.Quiet, () =>
        {
            form.Visibility = Shown(form.Visibility != Visibility.Visible);
            if (form.Visibility == Visibility.Visible) name.Focus();
        }));
        if (l.Name != "onsay")   // its settings are signing in and the chat key, under Settings and Advanced
        {
            var change = Button("Its settings", Kind.Quiet, () =>
            {
                if (fields.Children.Count == 0) Fields(fields, l.Name);
                fields.Visibility = Shown(fields.Visibility != Visibility.Visible);
            });
            change.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(change);
        }
        form.Children.Add(Label($"Share {Core.Title(l.Name)}"));
        form.Children.Add(Note("Their Onsay name, the one they signed up with, like james. They can use it from their own devices, and nothing else here."));
        form.Children.Add(new Border { BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Background = Card, Child = name });
        var part = "";
        if (l.Parts.Length > 1)
        {
            form.Children.Add(Label("What they can use"));
            form.Children.Add(SetupWindow.Chips(new[] { ("", "All of it") }.Concat(l.Parts.Select(x => (x, Core.Title(x.Trim('/'))))).ToArray(), () => part, v => part = v));
        }
        var go = Button("Share", Kind.Primary, () =>
        {
            var who = name.Text.Trim().ToLowerInvariant();
            if (!Regex.IsMatch(who, "^[a-z0-9_]+$") || who is "public" or "me") { said.Text = "An Onsay name is letters, digits and _, like james."; return; }
            var values = new JsonObject { ["lingo"] = l.Name, ["who"] = who };
            if (part.Length > 0) values["route"] = part;
            Did(Core.Control("share", values).why);
        });
        go.Margin = new Thickness(0, 12, 0, 0);
        form.Children.Add(go);
        into.Children.Add(buttons);
        into.Children.Add(said);
        into.Children.Add(form);
        into.Children.Add(fields);
    }

    /* 2. An app's settings as its openapi.json declares them, each with its value now, kept on this computer wherever the
          app keeps them. A secret is never shown, only whether it is set, and left empty it stays as it is. Saving writes
          only what changed, and an empty one goes back to its default. */
    static void Fields(StackPanel form, string lingo)
    {
        var (got, why) = Core.Control("settings", new() { ["lingo"] = lingo });
        if (got is not JsonArray fields) { form.Children.Add(Paragraph(why ?? "")); return; }
        if (fields.Count == 0) { var none = Note("This app has no settings."); none.Margin = new Thickness(0, 12, 0, 0); form.Children.Add(none); return; }
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
            status.Text = problem ?? (changed.Count == 0 ? "Nothing changed." : "Saved. The app reads them the next time it runs.");
        });
        form.Children.Add(new DockPanel { Margin = new Thickness(0, 12, 0, 0), Children = { save, status } });
    }

    ////////// 3. DEVICES //////////

    // 3. As Onsay lists them, asked off this thread, since it is a trip to Onsay.
    UIElement Devices()
    {
        var p = Page("Devices", "Every computer signed in to your Onsay account. Your devices can use the apps on each other, and nobody else's.");
        var (status, why) = Core.Onsay("status");
        if (status is null) { p.Children.Add(Paragraph(why ?? Helper)); return p; }
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
            if (got is not JsonArray devices) { list.Children.Add(Paragraph(problem ?? "")); return; }
            foreach (var d in devices)
            {
                bool online = (bool?)d?["online"] == true, self = $"{d?["name"]}" == $"{status["device"]}";
                var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = online ? Good : Ink4, VerticalAlignment = VerticalAlignment.Center };
                var when = online ? "Online now." : DateTime.TryParse($"{d?["seen"]}", out var seen) ? $"Last seen {seen.ToLocalTime():g}." : "Not seen yet.";
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
        if (policy is null) { p.Children.Add(Paragraph(why ?? Helper)); return p; }
        var local = policy["local"]?["overrides"]?["access"] as JsonArray;
        var people = (policy["access"] as JsonArray ?? new()).OfType<JsonObject>()
            .Where(r => $"{r["who"]}" is var w && w is not ("me" or "my devices" or "public") && !w.StartsWith("ip ") && $"{r["lingos"]}" != "none")
            .GroupBy(r => $"{r["who"]}");
        foreach (var person in people)
        {
            p.Children.Add(Section(person.Key));
            foreach (var rule in person)
            {
                var lingos = rule["lingos"] is JsonArray list ? string.Join(", ", list.Select(x => Core.Title($"{x}"))) : $"{rule["lingos"]}" is "*" ? "Every app" : Core.Title($"{rule["lingos"]}");
                var routes = (rule["routes"] as JsonArray)?.Select(r => Core.Title($"{r}".Trim('/'))).ToList();
                bool mine = local?.Any(r => JsonNode.DeepEquals(r, rule)) == true;
                var stop = mine ? Button("Stop sharing", Kind.Quiet, () =>
                {
                    Core.Control("unshare", new() { ["lingo"] = $"{rule["lingos"]}", ["who"] = person.Key });
                    Go("People");
                }) : null;
                p.Children.Add(Item(lingos, $"{(routes is { Count: > 0 } ? $"Only {string.Join(", ", routes)}." : "All of it.")}{(mine ? "" : " Shared from your account on onsay.dev.")}", stop));
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
        var handle = Core.Onsay("status").answer?["handle"] is { } h ? $"{h}" : null;
        var (policy, _) = Core.Control("policy");
        var local = policy?["local"]?["overrides"]?["access"] as JsonArray;
        DateTime? day = DateTime.MaxValue;
        foreach (var line in Enumerable.Reverse(Lines(Path.Combine(Core.Home, "decisions.log"))).Take(300))
        {
            JsonNode? d;
            try { d = JsonNode.Parse(line); } catch (JsonException) { continue; }
            var (caller, app, need, allowed, why) = ($"{d?["caller"]}", Core.Title($"{d?["lingo"]}"), d?["need"] is { } n ? $"{n}" : null, (bool?)d?["allowed"] == true, $"{d?["why"]}");
            var who = Core.Who(caller, handle);
            string title;
            if (need is null) title = allowed ? $"{who} used {app}" : $"{who} tried to use {app}";
            else
            {
                var (name, path) = need.Contains(' ') ? (need[..need.IndexOf(' ')], need[(need.IndexOf(' ') + 1)..]) : (need, null);
                var words = Core.Needs.FirstOrDefault(x => x.Name == name);
                var (did, wants) = (words.Did ?? name, words.Wants ?? name);
                if (path is not null) (did, wants) = (did.Replace("your files", path), wants.Replace("your files", path));
                title = allowed ? $"{app} {did}" : $"{app} wasn't allowed to {wants}";
                if (caller != "this computer") title += $", asked for by {(who.StartsWith("Your") || who.StartsWith("A ") || who == "Anyone" ? char.ToLower(who[0]) + who[1..] : who)}";
            }
            DateTime? at = DateTime.TryParse($"{d?["at"]}", out var t) ? t.ToLocalTime() : null;
            if (at?.Date != day)
            {
                day = at?.Date;
                var today = DateTime.Today;
                p.Children.Add(Section(day is not { } date ? "Earlier" : date == today ? "Today" : date == today.AddDays(-1) ? "Yesterday" : $"{date:dddd d MMMM}"));
            }
            // Someone's own device let in by sharing set here can be stopped from here.
            var shared = caller.Split('.') is [_, var them] && them != handle && allowed && why == $"rule: {them}"
                ? local?.OfType<JsonObject>().FirstOrDefault(r => $"{r["who"]}" == them && ($"{r["lingos"]}" == $"{d?["lingo"]}" || $"{r["lingos"]}" == "*")) : null;
            var sentence = new TextBlock { FontSize = 13, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            sentence.Inlines.Add(new Run(title + ". "));
            sentence.Inlines.Add(new Run(Because(why)) { Foreground = Ink3 });
            var time = new TextBlock { Text = at?.ToString("t") ?? "", Width = 76, FontSize = 12.5, Foreground = Ink3, Margin = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(time, System.Windows.Controls.Dock.Left);
            var row = new DockPanel { Children = { time } };
            if (shared is not null)
            {
                var stop = Button("Stop sharing", Kind.Quiet, () => { Core.Control("unshare", new() { ["lingo"] = $"{shared["lingos"]}", ["who"] = $"{shared["who"]}" }); Go("Activity"); });
                stop.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(stop, System.Windows.Controls.Dock.Right);
                row.Children.Add(stop);
            }
            row.Children.Add(sentence);
            p.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 9, 0, 9), Child = row });
        }
        if (day == DateTime.MaxValue) p.Children.Add(Paragraph("Nothing yet. When another of your devices or someone you share with uses an app here, or an app asks to do something that matters, it shows here."));
        return p;
    }

    ////////// 6. SETTINGS //////////

    UIElement General()
    {
        var p = Page("Settings", "Your account, how he looks, and Onsay on this computer.");

        // 6. As the onsay lingo says: who this computer is signed in as, and its name on Onsay.
        p.Children.Add(Section("Your account"));
        var (status, why) = Core.Onsay("status");
        if (status is null) p.Children.Add(Paragraph(why ?? Helper));
        else
        {
            if ((bool?)status["signed_in"] != true)
            {
                p.Children.Add(Paragraph("Not signed in. Sign in so he can answer you in the chat, and so your other devices can reach this one."));
                p.Children.Add(chat.SignInBox(() => Go("Settings")));
            }
            else
            {
                var said = Note("");
                p.Children.Add(Item(status["handle"] is { } handle ? $"Signed in as {handle}" : "Signed in", status["device"] is { } device ? $"This computer is {device}." : "It connects when Onsay is on.",
                    Button("Sign out", Kind.Quiet, () => { if (chat.SignOut() is { } problem) said.Text = problem; else Go("Settings"); })));
                p.Children.Add(said);
            }
        }

        p.Children.Add(Section("On this computer"));
        p.Children.Add(Item("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Toggle(Startup.IsOn, Startup.Set)));

        // 6. The same picker as setting him up, with him beside it. He on the taskbar changes too.
        p.Children.Add(Section("His look"));
        var preview = new Character.OnsayView { Width = 80, Height = 80, Margin = new Thickness(0, 12, 28, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(preview, System.Windows.Controls.Dock.Left);
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
        p.Children.Add(Item("Uninstall Onsay", "Takes the app, its shortcuts and its startup off this computer.", Button("Uninstall", Kind.Danger, () =>
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
        void Hint(string text) { var n = Note(text); (n.Margin, n.Foreground) = (new Thickness(0, 0, 0, 4), Ink2); p.Children.Add(n); }

        // A row with View, which opens the text read under it, newest first, with Copy and Open the file.
        void Log(string title, string under, Func<string> read, string? file = null)
        {
            var box = new Border { Visibility = Visibility.Collapsed, Background = Page_, BorderBrush = Rule, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 6, 0, 10) };
            Border view = null!;
            view = Button("View", Kind.Quiet, () =>
            {
                box.Visibility = Shown(box.Visibility != Visibility.Visible);
                ((TextBlock)view.Child).Text = box.Visibility == Visibility.Visible ? "Hide" : "View";
                if (box.Visibility != Visibility.Visible) return;
                var text = Selectable(11.5, Ink2);
                (text.FontFamily, text.Text) = (Mono, read() is { Length: > 0 } got ? got : "Nothing yet.");
                var copy = Button("Copy", Kind.Quiet, () => Clipboard.SetText(text.Text));
                var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 12), Children = { copy } };
                if (file is not null && File.Exists(file))
                {
                    var openFile = Button("Open the file", Kind.Quiet, () => Native.Open(file));
                    openFile.Margin = new Thickness(8, 0, 0, 0);
                    bar.Children.Add(openFile);
                }
                box.Child = new StackPanel { Children = { new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12, 10, 12, 10), Content = text }, bar } };
            });
            p.Children.Add(Item(title, under, view));
            p.Children.Add(box);
        }

        p.Children.Add(Section("What any app may do here"));
        Hint("Anything off, no app here can do, whatever it says or is allowed. Anything on, an app still asks you first.");
        if (policy?["ceiling"] is JsonArray ceiling)
        {
            var on = ceiling.Select(n => $"{n}").ToList();
            var said = Note("");
            said.Foreground = Bad;
            foreach (var (name, words, _, _) in Core.Needs)
                p.Children.Add(Item(words, null, Toggle(on.Contains(name), yes =>
                {
                    on.Remove(name);
                    if (yes) on.Add(name);
                    said.Text = Core.Control("ceiling", new() { ["needs"] = new JsonArray(on.Select(n => (JsonNode)n).ToArray()) }).why ?? "";
                })));
            p.Children.Add(said);
        }
        else p.Children.Add(Paragraph(why ?? Helper));

        p.Children.Add(Section("Who can reach this computer"));
        Hint("Anyone not listed gets nothing, as if this computer weren't there. You, on this computer, always can.");
        var local = policy?["local"]?["overrides"]?["access"] as JsonArray;
        foreach (var rule in (policy?["access"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var lingos = rule["lingos"] is JsonArray list ? string.Join(", ", list.Select(x => Core.Title($"{x}"))) : $"{rule["lingos"]}" switch { "*" => "Every app", "none" => "Nothing: kept out", var one => Core.Title(one) };
            var routes = (rule["routes"] as JsonArray)?.Select(r => Core.Title($"{r}".Trim('/'))).ToList() is { Count: > 0 } r ? $", only {string.Join(", ", r)}" : "";
            var from = (bool?)rule["default"] == true ? "Built in." : local?.Any(x => JsonNode.DeepEquals(x, rule)) == true ? "Set on this computer." : "From your account.";
            p.Children.Add(Item(Core.Who($"{rule["who"]}", null), $"{lingos}{routes}{($"{rule["lingos"]}" == "onsay" ? ", with your chat key" : "")}. {from}"));
        }
        foreach (var blocked in policy?["block"] as JsonArray ?? new()) p.Children.Add(Item($"A computer at {blocked}", "Blocked from everything."));
        var rules = Path.Combine(Core.Home, "policy.json");
        if (File.Exists(rules)) p.Children.Add(Item("This computer's own rules", "Kept in a file in Onsay's folder.", Button("Open", Kind.Quiet, () => Native.Open(rules))));
        if (policy is not null)
            Log("Every rule in force here", policy["account"]?["version"] is { } version ? $"Your account's rules are version {version}. This computer's own come first."
                : "Your account has no rules yet, so this computer uses its own.", () => policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        p.Children.Add(Section("Keys"));
        var copied = Note("");
        p.Children.Add(Item("This computer's key", status?["key"] is null ? "It gets one when it signs in." : "Proves a request really came from here. It never leaves this computer.",
            status?["key"] is { } key ? Button("Copy", Kind.Quiet, () => { Clipboard.SetText($"{key}"); copied.Text = "Copied this computer's key."; }) : null));
        p.Children.Add(Item("Chat key", "Lets another program send him a message. Keep it private.", Button("Copy", Kind.Quiet, () =>
        {
            var value = Core.Control("settings", new() { ["lingo"] = "onsay" }).answer?.AsArray().FirstOrDefault(s => $"{s?["name"]}" == "prompt_key")?["value"];
            if (value is null) { copied.Text = "There's no chat key yet. Onsay makes one the first time it is on."; return; }
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
        var sent = Path.Combine(Core.Home, "sent.log");
        Log("Everything sent to Onsay", "What you ask, and what each app asks for and answers.", () =>
            string.Join("\n\n", Enumerable.Reverse(Lines(sent)).Take(300).Select(line =>
            {
                JsonNode? s;
                try { s = JsonNode.Parse(line); } catch (JsonException) { return line; }
                var json = s?["message"]?.ToJsonString() ?? "";
                var kind = $"{s?["message"]?["type"]}";
                return $"{(DateTime.TryParse($"{s?["at"]}", out var at) ? at : default):g}  {Kinds.GetValueOrDefault(kind, kind)}\n{(json.Length > 4000 ? json[..4000] + "\n... (the rest is in the file)" : json)}";
            })), sent);
        var decisions = Path.Combine(Core.Home, "decisions.log");
        Log("Activity, as written", "Every request and every allow or refuse, the way the program writes it down.", () => string.Join("\n", Enumerable.Reverse(Lines(decisions)).Take(500)), decisions);
        Log("The program's log", "What the program that runs your apps says while it works, and any errors.", () => string.Join("\n", Enumerable.Reverse(Lines(Core.Log)).Take(500)), Core.Log);
        return p;
    }

    ////////// 8. PIECES //////////

    // 8. A heading over a few rows.
    static TextBlock Section(string text)
    {
        var heading = Label(text);
        (heading.FontSize, heading.Margin) = (15, new Thickness(0, 24, 0, 2));
        return heading;
    }

    /* 8. A row: a title, a quieter line under it, cut short past two lines, and a button, a switch or a dot at its
          end. With click, the whole row is a button that lights up under the pointer. */
    static Border Item(string title, string? under, FrameworkElement? end = null, Action? click = null)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = title, FontSize = 13.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap } } };
        if (under is not null) words.Children.Add(new TextBlock { Text = under, FontSize = 12, Foreground = Ink2, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.WordEllipsis, ToolTip = under });
        var row = new DockPanel();
        if (end is not null) { end.Margin = new Thickness(16, 0, 0, 0); DockPanel.SetDock(end, System.Windows.Controls.Dock.Right); row.Children.Add(end); }
        row.Children.Add(words);
        var b = new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 10, 8, 10), Margin = new Thickness(-8, 0, -8, 0), Background = Brushes.Transparent, Child = row };
        if (click is null) return b;
        b.Cursor = Cursors.Hand;
        Hover(b, Page_, Brushes.Transparent);
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    // 8. A log's lines, oldest first, read while the program may still be writing it. None when there is no file yet.
    static List<string> Lines(string path)
    {
        var lines = new List<string>();
        try
        {
            using var read = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (string? line; (line = read.ReadLine()) is not null;) lines.Add(line);
        }
        catch (IOException) { }
        return lines;
    }
}
