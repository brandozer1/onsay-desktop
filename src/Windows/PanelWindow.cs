// PanelWindow.cs - his panel: a small rounded window of his own that shows a page of HTML, a table, a chart or an answer
// laid out, drawn when a lingo calls the windows lingo's panel.show (Overlay.cs). One at a time: showing again replaces
// the page, and it stays where the person dragged it. The page runs in WebView2, with no reach to this computer: its
// links open in the browser, and it keeps nothing but its own browser storage, in %AppData%\Onsay\panel.
//
// 1. Show(html, title, w, h)   shows the page, sized in DIPs, beside him the first time
// 2. Close button, Esc         put it away, as panel.hide does
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using static Onsay.Desktop.ChatWindow;

namespace Onsay.Desktop;

sealed class PanelWindow : Window
{
    const double Head = 36;   // the title bar's height, in DIPs
    // A page given without its own document gets the chat's look, so a plain table or list reads well as it is.
    static string Css(SolidColorBrush b) => $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
    static readonly string Look = $"<meta charset=\"utf-8\"><style>html{{background:{Css(Card)};color-scheme:light}}body{{font:14px/1.5 'Segoe UI',sans-serif;color:{Css(Ink)};margin:14px 16px}}" +
        $"table{{border-collapse:collapse}}th,td{{border:1px solid {Css(Rule)};padding:4px 8px;text-align:left}}th{{background:{Css(Page_)}}}" +
        $"code,pre{{font-family:Consolas,monospace;background:{Css(Page_)};border-radius:6px}}pre{{padding:8px 10px;white-space:pre-wrap}}img{{max-width:100%}}</style>";

    readonly TextBlock title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly WebView2 web = new() { DefaultBackgroundColor = System.Drawing.Color.White };
    readonly TextBlock missing = new() { FontSize = 13, Foreground = Ink2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), Visibility = Visibility.Collapsed };
    Task? ready;
    bool placed;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public PanelWindow()
    {
        Title = "Onsay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Card;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // No frame, but its edges still resize it. A layered, see-through window can't hold a WebView2, so this one is whole.
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });

        var close = Button("\uE711", Kind.Icon, Hide, "Close");
        var head = new DockPanel { Height = Head, Margin = new Thickness(14, 0, 6, 0), Background = Brushes.Transparent, Children = { close, title } };
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var layout = new DockPanel();
        layout.Children.Add(head);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        layout.Children.Add(new Border { Height = 1, Background = Rule });
        DockPanel.SetDock(layout.Children[1], System.Windows.Controls.Dock.Top);
        layout.Children.Add(new Grid { Children = { web, missing } });
        Content = new Border { BorderBrush = Edge_, BorderThickness = new Thickness(1), Child = layout };

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Hide(); } };
        Closing += (_, e) => { if (Application.Current.MainWindow?.IsVisible == true) { e.Cancel = true; Hide(); } };
        SourceInitialized += (_, _) =>
        {
            // Rounded corners and a border in the card's colour, where Windows draws them (Windows 11). Square before that.
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2, edge = Edge_.Color.B << 16 | Edge_.Color.G << 8 | Edge_.Color.R;   // DWMWCP_ROUND; the colour as 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref edge, sizeof(int));
        };
    }

    ////////// 1. SHOWING A PAGE //////////

    // 1. Sizes are kept to what fits a screen. A page that is a whole document is shown as it is.
    public async void Show(string html, string heading, double w, double h)
    {
        title.Text = heading.Trim().Length > 0 ? heading : "Onsay";
        Width = Math.Clamp(w, 200, 1600);
        Height = Math.Clamp(h, 120, 1200) + Head;
        if (!placed) Place();
        Show();
        try
        {
            await (ready ??= Ready());
            web.NavigateToString(html.Contains("<html", StringComparison.OrdinalIgnoreCase) ? html : Look + html);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or ArgumentException)
        {
            ready = null;
            web.Visibility = Visibility.Collapsed;
            missing.Visibility = Visibility.Visible;
            missing.Text = e is WebView2RuntimeNotFoundException
                ? "This page needs Microsoft Edge WebView2, which comes with Windows 11. Get it from go.microsoft.com/fwlink/p/?LinkId=2124703 and I'll show it."
                : $"I couldn't show this page: {e.Message}";
        }
    }

    // Its browser, made once: nothing of the computer reachable from the page, and every link out opened in the browser.
    async Task Ready()
    {
        var home = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Folder, "panel"));
        await web.EnsureCoreWebView2Async(home);
        var core = web.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith("data:") || e.Uri.StartsWith("about:")) return;
            e.Cancel = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "mailto") Native.Open(u.ToString());
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "mailto") Native.Open(u.ToString());
        };
    }

    // Above him, its right edge with his, or below him when there is no room above, kept inside his screen's work area.
    void Place()
    {
        placed = true;
        var him = Application.Current.MainWindow;
        if (him is null) { WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        Native.GetWindowRect(new WindowInteropHelper(him).Handle, out var body);
        var work = Native.MonitorAt(body.Center, Native.MONITOR_DEFAULTTONEAREST).rcWork;
        double scale = VisualTreeHelper.GetDpi(him).DpiScaleX, pw = Width * scale, ph = Height * scale, room = 8 * scale;
        double x = Math.Clamp(body.Right - pw, work.Left + room, Math.Max(work.Left + room, work.Right - pw - room));
        double y = body.Top - ph - room >= work.Top + room ? body.Top - ph - room : Math.Min(body.Bottom + room, work.Bottom - ph - room);
        (Left, Top) = (x / scale, Math.Max(work.Top + room, y) / scale);
    }
}
