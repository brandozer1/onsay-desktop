// SettingsWindow.cs - Onsay's window, from his right click menu or the chat's: a list of pages down the left, the page on
// the right. One window at a time, above his chat, and every change takes effect as it is made. Every word on it is the
// person's: a lingo is an app, an access rule is sharing, a grant is allowing.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on the page named
// 2. Apps        every app on this computer: on or off, what it can do, who can use it, Share, and its own settings
//    Fields      an app's own settings, as its openapi.json declares them
// 3. Devices     the computers on the account, online or not
// 4. People      who each app is shared with, and Stop sharing
// 5. Activity    what reached this computer and what apps were allowed or refused, newest first
// 6. Settings    the account, starting with Windows, his look, updates and removing Onsay
// 7. Advanced    the most any app may do, who can reach this computer, the account's rules, keys, folders, the program
//                that runs the apps, and everything sent to Onsay
// 8. Off, Item   the line when Onsay is turned off here, and a row of a list
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
    readonly ScrollViewer page = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(26, 22, 26, 22) };
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

    static StackPanel Page(string title, string about) => new()
    {
        MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left,
        Children = { new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 10) }, Paragraph(about) },
    };

    const string Helper = "Onsay's helper on this computer isn't answering right now. Try again in a moment.";

    ////////// 2. APPS //////////

    // 2. Each app: its switch, then Details and Share, which open what it can do and who can use it under it.
    UIElement Apps()
    {
        var p = Page("Apps", "Apps are how Onsay does things on this computer. Turn one off and nothing can use it, not even him.");
        var lingos = Core.Lingos();
        if (lingos is null) { p.Children.Add(Paragraph(Helper)); return p; }
        if (lingos.Count == 0) p.Children.Add(Paragraph("No apps yet. To add one, put it in the apps folder, under Advanced."));
        foreach (var l in lingos)
        {
            if (l.Error is not null)
            {
                p.Children.Add(Label(Core.Title(l.Name)));
                var why = Paragraph($"It can't run because of a problem in it: {l.Error}");
                why.Foreground = Bad;
                p.Children.Add(why);
                continue;
            }
            // Its first sentence, so the list reads at a glance.
            var first = l.About.Split(". ")[0].TrimEnd('.');
            var said = Note("");
            (said.Margin, said.Visibility) = (new Thickness(0, 6, 12, 0), Visibility.Collapsed);
            var more = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, -56, 0) };
            void Open(bool sharing)
            {
                more.Visibility = sharing || more.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
                if (more.Visibility == Visibility.Visible) Details(more, l, sharing);
            }
            var share = Button("Share", Kind.Quiet, () => Open(true));
            share.Margin = new Thickness(8, 0, 0, 0);
            var below = new StackPanel { Children = { said, new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { Button("Details", Kind.Quiet, () => Open(false)), share } }, more } };
            p.Children.Add(Switch(Core.Title(l.Name), first.Length > 0 ? first + "." : null, l.Running, on =>
            {
                said.Text = Core.Control(on ? "start" : "stop", new() { ["lingo"] = l.Name }).why
                    ?? (l.Name == "onsay" && !on ? "Off. He can't answer you in the chat, and your other devices can't reach this computer, until you turn it on again." : "");
                said.Visibility = said.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }, below));
        }
        return p;
    }

    /* 2. What it can do: each thing that matters it says it needs, allowed without asking or asked first, as a switch, and
          what it was allowed for one folder. Who can use it: you, then each rule that lets someone in, with Stop sharing on
          those set here. A change builds it again, so it always shows what is true. */
    void Details(StackPanel into, Core.Lingo l, bool sharing)
    {
        into.Children.Clear();
        var (policy, why) = Core.Control("policy");
        if (policy is null) { into.Children.Add(Paragraph(why ?? Helper)); return; }
        var said = Note("");
        said.Foreground = Bad;
        void Did(string? problem) { if (problem is null) Details(into, l, false); else said.Text = problem; }
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
            var inside = need.StartsWith("files") ? "In its own folder it never asks. " : "";
            into.Children.Add(Switch(words, inside + (on ? "Allowed without asking." : "Asks you first."), on, yes =>
                Did(Core.Control(yes ? "grant" : "ungrant", new() { ["lingo"] = l.Name, ["need"] = need }).why is { } problem
                    ? here.Contains(need) || yes ? problem : "That was allowed from your account on onsay.dev, so it changes there." : null)));
        }
        foreach (var g in granted.Where(g => g.Contains(' ')))
        {
            var (need, path) = (g[..g.IndexOf(' ')], g[(g.IndexOf(' ') + 1)..]);
            into.Children.Add(Switch($"{(need == "files.read" ? "Read" : "Change")} files in {path}", "Allowed without asking.", true, _ =>
                Did(Core.Control("ungrant", new() { ["lingo"] = l.Name, ["need"] = g }).why is { } problem
                    ? here.Contains(g) ? problem : "That was allowed from your account on onsay.dev, so it changes there." : null)));
        }
        if (l.Needs.Contains("net")) into.Children.Add(Note(ceiling.Contains("net") ? "It can reach the internet." : "It can't reach the internet: that is turned off for every app, under Advanced."));
        else if (asks.Count == 0) into.Children.Add(Note("It needs nothing from this computer: not your screen, your files or your programs."));

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

        // Share: their Onsay name, and all of it or one part, picked.
        var form = new StackPanel { Visibility = sharing ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        var open = Button("Share", Kind.Quiet, () => { form.Visibility = Visibility.Visible; });
        open.Margin = new Thickness(0, 10, 0, 0);
        open.Visibility = sharing ? Visibility.Collapsed : Visibility.Visible;
        var name = new TextBox { BorderThickness = new Thickness(0), FontSize = 13, Background = Brushes.Transparent, Foreground = Ink };
        form.Children.Add(Label($"Share {Core.Title(l.Name)} with someone"));
        form.Children.Add(Note("Their Onsay name, the one they signed up with, like james. They can use it from their own devices, and nothing else here."));
        form.Children.Add(new Border { BorderBrush = Edge_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Background = Card, Child = name });
        var part = "";
        if (l.Parts.Length > 1)
        {
            var pick = Label("What they can use");
            form.Children.Add(pick);
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
        var cancel = Button("Cancel", Kind.Quiet, () => { form.Visibility = Visibility.Collapsed; open.Visibility = Visibility.Visible; });
        cancel.Margin = new Thickness(8, 0, 0, 0);
        form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Children = { go, cancel } });
        into.Children.Add(open);
        into.Children.Add(form);
        into.Children.Add(said);
        if (sharing) Dispatcher.BeginInvoke(() => name.Focus());

        if (l.Name == "onsay") return;   // its settings are signing in and the chat key, under Settings and Advanced
        var fields = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        var change = Button("Change its settings", Kind.Quiet, () =>
        {
            if (fields.Children.Count == 0) Fields(fields, l.Name);
            fields.Visibility = fields.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        });
        change.Margin = new Thickness(0, 14, 0, 0);
        into.Children.Add(change);
        into.Children.Add(fields);
    }

    /* 2. An app's settings as its openapi.json declares them, each with its value now, kept on this computer wherever the
          app keeps them. A secret is never shown, only whether it is set, and left empty it stays as it is. Saving writes
          only what changed, and an empty one goes back to its default. */
    static void Fields(StackPanel form, string lingo)
    {
        var (got, why) = Core.Control("settings", new() { ["lingo"] = lingo });
        if (got is not JsonArray fields) { form.Children.Add(Paragraph(why ?? "")); return; }
        if (fields.Count == 0) { form.Children.Add(Paragraph("This app has no settings.")); return; }
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
        if (Off(status) is { } off) p.Children.Add(off);
        if ((bool?)status["signed_in"] != true)
        {
            p.Children.Add(Paragraph("Sign in to see the computers on your account."));
            p.Children.Add(chat.SignInBox(() => Go("Devices")));
            return p;
        }
        var list = new StackPanel { Children = { Note("Looking up your devices...") } };
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
            var name = Label(person.Key);
            name.FontSize = 15;
            p.Children.Add(name);
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
            p.Children.Add(Paragraph("You haven't shared anything yet. To share an app, press Share on it in Apps."));
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
          anyone but this computer, let in or not, and each thing an app was allowed or refused. The newest 300. */
    UIElement Activity()
    {
        var p = Page("Activity", "What reached this computer from your other devices and from people you share with, and what your apps were allowed to do. Newest first.");
        p.Children.Add(Button("Refresh", Kind.Quiet, () => Go("Activity")));
        var handle = Core.Onsay("status").answer?["handle"] is { } h ? $"{h}" : null;
        var (policy, _) = Core.Control("policy");
        var local = policy?["local"]?["overrides"]?["access"] as JsonArray;
        var lines = new List<string>();
        try
        {
            using var read = new StreamReader(new FileStream(Path.Combine(Core.Home, "decisions.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (string? line; (line = read.ReadLine()) is not null;) lines.Add(line);
        }
        catch (IOException) { }   // nothing yet
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        p.Children.Add(list);
        foreach (var line in Enumerable.Reverse(lines).Take(300))
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
            var at = DateTime.TryParse($"{d?["at"]}", out var t) ? $" {t.ToLocalTime():g}" : "";
            // Someone's own device let in by sharing set here can be stopped from here.
            var shared = caller.Split('.') is [_, var them] && them != handle && allowed && why == $"rule: {them}"
                ? local?.OfType<JsonObject>().FirstOrDefault(r => $"{r["who"]}" == them && ($"{r["lingos"]}" == $"{d?["lingo"]}" || $"{r["lingos"]}" == "*")) : null;
            var stop = shared is null ? null : Button("Stop sharing", Kind.Quiet, () => { Core.Control("unshare", new() { ["lingo"] = $"{shared["lingos"]}", ["who"] = $"{shared["who"]}" }); Go("Activity"); });
            list.Children.Add(Item(title + ".", Because(why) + at, stop));
        }
        if (list.Children.Count == 0) list.Children.Add(Paragraph("Nothing yet. When another of your devices or someone you share with uses an app here, or an app asks to do something that matters, it shows here."));
        return p;
    }

    ////////// 6. SETTINGS //////////

    UIElement General()
    {
        var p = Page("Settings", "Your account, how he looks, and Onsay on this computer.");

        // 6. As the onsay lingo says: who this computer is signed in as, and its name on Onsay.
        p.Children.Add(Label("Your account"));
        var (status, why) = Core.Onsay("status");
        if (status is null) p.Children.Add(Paragraph(why ?? Helper));
        else
        {
            if (Off(status) is { } off) p.Children.Add(off);
            if ((bool?)status["signed_in"] != true)
            {
                p.Children.Add(Paragraph("Not signed in. Sign in so he can answer you in the chat, and so your other devices can reach this one."));
                p.Children.Add(chat.SignInBox(() => Go("Settings")));
            }
            else
            {
                p.Children.Add(Paragraph(status["handle"] is { } handle ? $"You're {handle}. This computer is {status["device"]}."
                    : "This computer is signed in to Onsay, and connects when Onsay is on."));
                var said = Note("");
                p.Children.Add(Button("Sign out", Kind.Quiet, () => { if (chat.SignOut() is { } problem) said.Text = problem; else Go("Settings"); }));
                p.Children.Add(said);
            }
        }

        p.Children.Add(Label("This computer"));
        p.Children.Add(Switch("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Startup.IsOn, Startup.Set));

        // 6. The same picker as setting him up, with him beside it. He on the taskbar changes too.
        p.Children.Add(Label("His look"));
        var preview = new Character.OnsayView { Width = 96, Height = 96, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(preview, System.Windows.Controls.Dock.Left);
        p.Children.Add(new DockPanel { Children = { preview, SetupWindow.LookPicker(settings, () => preview.React("yes")) } });

        p.Children.Add(Label("About Onsay"));
        if (!Updates.Installed)
        {
            p.Children.Add(Paragraph("This copy was built on this computer, so it does not update itself."));
            return p;
        }
        p.Children.Add(Paragraph($"Version {Updates.Version}. Onsay keeps itself up to date on its own: a newer version is downloaded quietly and goes in the next time you quit."));
        var checking = Note("");
        checking.Visibility = Visibility.Collapsed;
        p.Children.Add(Button("Check for updates now", Kind.Quiet, async () =>
        {
            checking.Text = "Checking...";
            checking.Visibility = Visibility.Visible;
            checking.Text = await Updates.CheckNow();
        }));
        p.Children.Add(checking);
        p.Children.Add(Label("Remove Onsay"));
        p.Children.Add(Paragraph("Takes Onsay off this computer: the app, its shortcuts and its startup."));
        p.Children.Add(Button("Uninstall Onsay", Kind.Danger, () =>
        {
            Updates.Uninstall();
            Application.Current.Shutdown();
        }));
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

    /* 7. Everything underneath, in plain labels: the ceiling, the access rules and blocks in force, the account's policy,
          this device's key and the prompt key, the folders, the lingo client's own files, and sent.log, which the onsay
          lingo keeps in the lingo client's home, a line each, oldest first, shown newest first, the newest 500. */
    UIElement Advanced()
    {
        var p = Page("Advanced", "How Onsay works underneath, for anyone who wants to look. Nothing here needs changing for him to work.");
        var (policy, why) = Core.Control("policy");
        var status = Core.Onsay("status").answer;

        p.Children.Add(Label("The most any app may do here"));
        p.Children.Add(Note("Anything off, no app on this computer can do, whatever it says or is allowed. What is on, an app still asks you first for anything that matters."));
        if (policy?["ceiling"] is JsonArray ceiling)
        {
            var on = ceiling.Select(n => $"{n}").ToList();
            var said = Note("");
            foreach (var (name, words, _, _) in Core.Needs)
                p.Children.Add(Switch(words, null, on.Contains(name), yes =>
                {
                    on.Remove(name);
                    if (yes) on.Add(name);
                    said.Text = Core.Control("ceiling", new() { ["needs"] = new JsonArray(on.Select(n => (JsonNode)n).ToArray()) }).why ?? "";
                }));
            p.Children.Add(said);
        }
        else p.Children.Add(Paragraph(why ?? Helper));

        p.Children.Add(Label("Who can reach this computer"));
        p.Children.Add(Note("Anyone not listed gets nothing, as if this computer weren't there. You, on this computer, always can."));
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
        if (File.Exists(rules)) p.Children.Add(Button("Open this computer's rules file", Kind.Quiet, () => Native.Open(rules)));

        p.Children.Add(Label("Your account's rules"));
        p.Children.Add(Paragraph(policy?["account"]?["version"] is { } version ? $"Version {version}, from Onsay and checked here. This computer's own rules come first, and nothing from Onsay goes past the switches above."
            : "None yet. This computer uses its own rules until your account has some."));
        if (policy is not null)
        {
            var all = Selectable(11.5, Ink2);
            all.Text = policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            (all.Background, all.FontFamily, all.Padding, all.Margin) = (Page_, Mono, new Thickness(8), new Thickness(0, 4, 0, 0));
            p.Children.Add(new Expander { Header = "The rules in force here, in full", Content = all, Foreground = Ink, Margin = new Thickness(0, 4, 0, 0) });
        }

        p.Children.Add(Label("Keys"));
        if (status?["key"] is { } key)
        {
            p.Children.Add(Note("This computer's key. Onsay vouches for it, so your other devices know a request really came from here. It never leaves this computer."));
            var shown = Selectable(12, Ink2);
            shown.Text = $"{key}";
            shown.FontFamily = Mono;
            p.Children.Add(shown);
        }
        else p.Children.Add(Note("This computer gets its key when it signs in."));
        var copied = Note("");
        var chatKey = Button("Copy the chat key", Kind.Quiet, () =>
        {
            var value = Core.Control("settings", new() { ["lingo"] = "onsay" }).answer?.AsArray().FirstOrDefault(s => $"{s?["name"]}" == "prompt_key")?["value"];
            if (value is null) { copied.Text = "There's no chat key yet. Onsay makes one the first time it is on."; return; }
            Clipboard.SetText($"{value}");
            copied.Text = "Copied. Keep it private.";
        });
        chatKey.Margin = new Thickness(0, 10, 0, 4);
        p.Children.Add(chatKey);
        p.Children.Add(Note($"Another program can send him a message at {Core.Url}/onsay with the chat key."));
        p.Children.Add(copied);

        p.Children.Add(Label("Folders"));
        var apps = Button("Open the apps folder", Kind.Quiet, () => Native.Open(Core.LingosFolder));
        apps.Margin = new Thickness(8, 0, 0, 0);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Button("Open Onsay's folder", Kind.Quiet, () => Native.Open(Settings.Folder)), apps } });
        p.Children.Add(Note("To add an app, put its .lingo file in the apps folder. It shows in Apps, off until you turn it on."));

        p.Children.Add(Label("The program that runs your apps"));
        p.Children.Add(Paragraph($"It answers at {Core.Url}, for this computer alone until you share something. Its settings file names its folder and, when you write one there, its port."));
        var log = Button("Open its log", Kind.Quiet, () => { if (File.Exists(Core.Log)) Native.Open(Core.Log); });
        log.Margin = new Thickness(8, 0, 0, 0);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Button("Open its settings file", Kind.Quiet, () => Native.Open(Core.Config)), log } });

        p.Children.Add(Label("Everything sent to Onsay"));
        p.Children.Add(Paragraph("Everything this computer sends Onsay is kept here, newest first, exactly as it went, so you can see for yourself: " +
            "the things you ask, and what each app asks for and answers as it runs. Your files, passwords and screen stay here unless you ask him to send something."));
        var path = Path.Combine(Core.Home, "sent.log");
        var file = Button("Open the log file", Kind.Quiet, () => { if (File.Exists(path)) Native.Open(path); });
        file.Margin = new Thickness(8, 0, 0, 0);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { Button("Refresh", Kind.Quiet, () => Go("Advanced")), file } });
        var lines = new List<string>();
        try
        {
            using var read = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (string? line; (line = read.ReadLine()) is not null;) lines.Add(line);
        }
        catch (IOException) { }   // none sent yet
        var count = 0;
        foreach (var line in Enumerable.Reverse(lines))
        {
            JsonNode? sent;
            try { sent = JsonNode.Parse(line); } catch (JsonException) { continue; }
            if (sent?["message"] is not JsonObject m) continue;
            if (++count > 500) break;
            var kind = $"{m["type"]}";
            var head = new TextBlock { FontSize = 12.5, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new Run($"{(DateTime.TryParse($"{sent["at"]}", out var at) ? at : default):g}  ") { Foreground = Ink3, FontFamily = Mono });
            head.Inlines.Add(new Run(Kinds.GetValueOrDefault(kind, kind)));
            var json = m.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var body = Selectable(11.5, Ink2);
            body.Text = json.Length > 20_000 ? json[..20_000] + "\n... (the rest is in the log file)" : json;
            (body.Background, body.FontFamily, body.Padding, body.Margin) = (Page_, Mono, new Thickness(8), new Thickness(0, 4, 0, 0));
            p.Children.Add(new Expander { Header = head, Content = body, Margin = new Thickness(0, 0, 0, 6), Foreground = Ink });
        }
        if (count == 0) p.Children.Add(Paragraph("Nothing sent yet."));
        return p;
    }

    ////////// 8. PIECES //////////

    /* 8. With the onsay lingo stopped, this computer has no connection to Onsay: a plain line saying so, and turning it on.
          Signing in still works while it is off. Null while it is on. */
    UIElement? Off(JsonNode status)
    {
        if ((bool?)status["running"] != false) return null;
        var on = Button("Turn Onsay on", Kind.Primary, () =>
        {
            Core.Control("start", new() { ["lingo"] = "onsay" });
            Go(pages.First(x => x.Row.Tag is not null).Name);
        });
        return new Border
        {
            Background = Butter, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 4, 0, 12),
            Child = new StackPanel { Children = { Paragraph("Onsay is turned off on this computer, so he can't answer you in the chat and your other devices can't reach it."), on } },
        };
    }

    // 8. A row of a list: a title, a quieter line under it, and a button or a dot at its end.
    static Border Item(string title, string? under, UIElement? end = null)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap } } };
        if (under is not null) words.Children.Add(new TextBlock { Text = under, FontSize = 12.5, Foreground = Ink3, Margin = new Thickness(0, 2, 12, 0), TextWrapping = TextWrapping.Wrap });
        var row = new DockPanel();
        if (end is not null) { DockPanel.SetDock(end, System.Windows.Controls.Dock.Right); row.Children.Add(end); }
        row.Children.Add(words);
        return new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = row };
    }
}
