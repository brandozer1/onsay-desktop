// SettingsWindow.cs - his settings, from his right click menu or the chat's: a list of pages down the left, the page on
// the right. One window at a time, above his chat, and every change takes effect as it is made.
//
// 1. Open(settings, chat, page)   shows it, or brings the one already open to the front, on the page named
// 2. Settings    the account, starting with Windows, his look, updates and removing Onsay
// 3. Advanced    this computer's key, its folders, and the logs to read
// 4. Pieces      Section, a heading; Item, a row; Boxed, an input in its border; Lines, a log's newest lines
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    public static void Open(Settings settings, ChatWindow chat, string page = "Settings")
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

        Add("Settings", General);
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

        // The wheel scrolls the page wherever the pointer is, over a text box too, which would otherwise keep it. A box that
        // scrolls on its own, like a log, scrolls first, and the page once it reaches its end.
        page.PreviewMouseWheel += (_, e) =>
        {
            bool up = e.Delta > 0;
            var under = e.OriginalSource as DependencyObject;
            while (under is not null && under != page)
            {
                if (under is ScrollViewer inner && inner.ScrollableHeight > 0)
                {
                    if (up && inner.VerticalOffset > 0) return;
                    if (!up && inner.VerticalOffset < inner.ScrollableHeight) return;
                }
                if (under is Visual) under = VisualTreeHelper.GetParent(under);
                else under = LogicalTreeHelper.GetParent(under);
            }
            page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
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
            FontFamily = Display,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var line = Paragraph(about);
        line.Margin = new Thickness(0, 0, 0, 10);
        return new StackPanel { MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left, Children = { head, line } };
    }

    const string Helper = "Onsay's helper on this computer isn't answering right now. Try again in a moment.";

    ////////// 2. SETTINGS //////////

    // 2.
    UIElement General()
    {
        var p = Page("Settings", "Your account, how he looks, and Onsay on this computer.");

        // As Core.cs says: whether Onsay knows this computer, and its name there.
        p.Children.Add(Section("Your account"));
        var status = Core.Status();
        if ((bool?)status["signed_in"] != true)
        {
            p.Children.Add(Paragraph("Not signed in. Add this computer to your account so he can answer you in the chat, and so your other devices can reach this one."));
            p.Children.Add(chat.SignInBox(() => Go("Settings")));
        }
        else
        {
            var said = Note("");
            var signOut = Button("Sign out", Kind.Quiet, () =>
            {
                var problem = chat.SignOut();
                if (problem is not null) said.Text = problem;
                else Go("Settings");
            });
            p.Children.Add(Item($"Signed in as {status["handle"]}", $"This computer is {status["device"]}.", signOut));
            p.Children.Add(said);
        }

        p.Children.Add(Section("On this computer"));
        p.Children.Add(Item("Start Onsay with Windows", "He appears on your taskbar when you sign in to Windows.", Toggle(Startup.IsOn, Startup.Set)));
        p.Children.Add(Item("Quit Onsay", "Closing him puts him by the clock and leaves Onsay running, so your devices can still use this computer. Quitting stops it until you open Onsay again.", Button("Quit", Kind.Quiet, () => Application.Current.MainWindow?.Close())));

        // The same picker as setting him up, with him beside it. He on the taskbar changes too.
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
            p.Children.Add(Item($"Version {Core.Version}", "Built on this computer, so this copy doesn't update itself."));
            return p;
        }
        // Update now checks at once and, with a newer version, puts it in and starts him again on it; else it says why not.
        var came = Note("");
        Border? update = null;
        update = Button("Update now", Kind.Quiet, async () =>
        {
            var label = (TextBlock)update!.Child;
            if (label.Text == "Checking...") return;
            label.Text = "Checking...";
            came.Text = await Updates.Now();
            label.Text = "Update now";
        });
        p.Children.Add(Item($"Version {Core.Version}", "Updates by itself: a newer version downloads quietly and goes in while he isn't busy. Update now gets it straight away and starts him again.", update));
        p.Children.Add(came);
        p.Children.Add(Section("Remove Onsay"));
        p.Children.Add(Item("Uninstall Onsay", "Takes the app, its shortcuts and its startup off this computer, and asks whether your settings and chats go too.", Button("Uninstall", Kind.Danger, () =>
        {
            Updates.Uninstall();
            Application.Current.Shutdown();
        })));
        return p;
    }

    ////////// 3. ADVANCED //////////

    /* 3. Everything underneath, a row each in plain labels: this device's key, the folders, and the logs, each read in
          place: requests.log, every call in and out, which is what the port sends Onsay, sealed, and port.log, what it
          says while it works. */
    UIElement Advanced()
    {
        var p = Page("Advanced", "How Onsay works underneath, for anyone who wants to look. Nothing here needs changing for him to work.");
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

        p.Children.Add(Section("Keys"));
        var copied = Note("");
        var key = $"{Core.Status()["key"]}";
        p.Children.Add(Item("This computer's key", "Proves a request really came from here. Its private half never leaves this computer.", Button("Copy", Kind.Quiet, () =>
        {
            Clipboard.SetText(key);
            copied.Text = "Copied this computer's key.";
        })));
        p.Children.Add(copied);

        p.Children.Add(Section("Folders"));
        p.Children.Add(Item("Onsay's folder", "Everything he keeps on this computer.", Button("Open", Kind.Quiet, () => Native.Open(Settings.Folder))));
        p.Children.Add(Item("Who else may use this computer", "policy.json in the port's folder: which routes others may use, like { \"/notes\": [\"james\"] }. With none, only you.", Button("Open", Kind.Quiet, () => Native.Open(Core.Home))));

        p.Children.Add(Section("Logs"));
        Hint("Newest first, exactly as written.");
        var requests = Path.Combine(Core.Home, "requests.log");
        Log("Every request", "Everything asked of this computer and everything it asked, the way the port writes it down. This is what it sends Onsay, sealed so only Onsay can read it.", () => string.Join("\n", Lines(requests, 500)), requests);
        Log("The port's log", "What the port says while it works, and any errors.", () => string.Join("\n", Lines(Core.Log, 500)), Core.Log);
        return p;
    }

    ////////// 4. PIECES //////////

    // 4. A heading over a few rows.
    static TextBlock Section(string text)
    {
        var heading = Label(text);
        heading.FontFamily = Display;
        heading.FontWeight = FontWeights.Bold;
        heading.FontSize = 16;
        heading.Margin = new Thickness(0, 24, 0, 2);
        return heading;
    }

    /* 7. A row: a title, a quieter line under it, cut short past two lines, and a button, a switch or a dot at its
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

    // 4. A text box or password box in its border. Also setting him up's key field.
    internal static Border Boxed(UIElement input) => new()
    {
        BorderBrush = Edge_,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(6, 4, 6, 4),
        Background = Card,
        Child = input,
    };

    // 4. A log's newest lines, newest first, read while the program may still be writing it. None when there is no file yet.
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
