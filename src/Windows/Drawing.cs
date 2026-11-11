// Drawing.cs - draws a screen here from its instructions, the part of Canvas 2D the screen lingo takes (screen.draw in
// Host.cs), as a picture WPF shows: paths, rectangles, text in its CSS font and
// colour, pictures inside data: addresses, transforms, clips, save and restore. Nothing here reaches the web.
//
// 1. Draw(instructions)   -> the screen as a frozen picture, sized by its clear, so any thread may make it
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Onsay.Desktop;

static class Drawing
{
    // Pictures by their data: address, read once, so a screen drawn again doesn't read them again.
    static readonly ConcurrentDictionary<string, BitmapSource?> pictures = new();

    // How to draw now, kept by save and taken back by restore with every push since.
    sealed class State
    {
        public Brush Fill = Brushes.Black;
        public Brush Stroke = Brushes.Black;
        public double Line = 1;
        public double Alpha = 1;
        public Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public double Size = 10;
        public string Align = "start";
        public string Baseline = "alphabetic";
        public int Pushed;
        public State Copy() => (State)MemberwiseClone();
    }

    ////////// 1. DRAWING //////////

    // 1. An instruction that isn't understood is passed over, so a newer lingo still draws what this app knows.
    public static (ImageSource picture, double width, double height) Draw(IReadOnlyList<JsonNode?> instructions)
    {
        double width = 300;
        double height = 150;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var now = new State();
            var saved = new Stack<State>();
            var path = new PathGeometry();
            PathFigure? figure = null;
            foreach (var one in instructions)
            {
                if (one is not JsonObject o || o.Count != 1) continue;
                var (key, value) = o.First();
                var a = value as JsonArray ?? new JsonArray();
                double N(int i) => i < a.Count && a[i] is JsonValue v && v.TryGetValue(out double d) ? d : 0;
                bool Has(int i) => i < a.Count && a[i] is JsonValue;
                try
                {
                    switch (key)
                    {
                        case "clear":
                            if (Has(0) && Has(1))
                            {
                                width = Math.Max(1, N(0));
                                height = Math.Max(1, N(1));
                            }
                            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
                            break;
                        case "fillStyle": now.Fill = Paint($"{value}"); break;
                        case "strokeStyle": now.Stroke = Paint($"{value}"); break;
                        case "lineWidth": now.Line = (double?)value ?? 1; break;
                        case "globalAlpha": now.Alpha = Compat.Clamp((double?)value ?? 1, 0, 1); break;
                        case "font": (now.Face, now.Size) = Font($"{value}"); break;
                        case "textAlign": now.Align = $"{value}"; break;
                        case "textBaseline": now.Baseline = $"{value}"; break;
                        case "save":
                            saved.Push(now);
                            now = now.Copy();
                            now.Pushed = 0;
                            break;
                        case "restore":
                            for (; now.Pushed > 0; now.Pushed--) dc.Pop();
                            if (saved.Count > 0) now = saved.Pop();
                            break;
                        case "translate": Push(dc, now, () => dc.PushTransform(new TranslateTransform(N(0), N(1)))); break;
                        case "scale": Push(dc, now, () => dc.PushTransform(new ScaleTransform(N(0), N(1)))); break;
                        case "rotate": Push(dc, now, () => dc.PushTransform(new RotateTransform(N(0) * 180 / Math.PI))); break;
                        case "beginPath":
                            path = new PathGeometry();
                            figure = null;
                            break;
                        case "closePath":
                            if (figure is not null) figure.IsClosed = true;
                            break;
                        case "moveTo":
                            figure = new PathFigure { StartPoint = new Point(N(0), N(1)) };
                            path.Figures.Add(figure);
                            break;
                        case "lineTo": To(path, ref figure, new Point(N(0), N(1)), f => f.Segments.Add(new LineSegment(new Point(N(0), N(1)), true))); break;
                        case "quadraticCurveTo": To(path, ref figure, new Point(N(0), N(1)), f => f.Segments.Add(new QuadraticBezierSegment(new Point(N(0), N(1)), new Point(N(2), N(3)), true))); break;
                        case "bezierCurveTo": To(path, ref figure, new Point(N(0), N(1)), f => f.Segments.Add(new BezierSegment(new Point(N(0), N(1)), new Point(N(2), N(3)), new Point(N(4), N(5)), true))); break;
                        case "arc": Arc(path, ref figure, N(0), N(1), N(2), N(3), N(4), a.Count > 5 && (bool?)a[5] == true); break;
                        case "rect": Box(path, ref figure, new Rect(N(0), N(1), Math.Max(0, N(2)), Math.Max(0, N(3))), 0); break;
                        case "roundRect": Box(path, ref figure, new Rect(N(0), N(1), Math.Max(0, N(2)), Math.Max(0, N(3))), N(4)); break;
                        case "fill": Faded(dc, now, () => dc.DrawGeometry(now.Fill, null, path.Clone())); break;
                        case "stroke": Faded(dc, now, () => dc.DrawGeometry(null, new Pen(now.Stroke, now.Line), path.Clone())); break;
                        case "clip": Push(dc, now, () => dc.PushClip(path.Clone())); break;
                        case "fillRect": Faded(dc, now, () => dc.DrawRectangle(now.Fill, null, Sized(N(0), N(1), N(2), N(3)))); break;
                        case "strokeRect": Faded(dc, now, () => dc.DrawRectangle(null, new Pen(now.Stroke, now.Line), Sized(N(0), N(1), N(2), N(3)))); break;
                        case "clearRect": dc.DrawRectangle(Brushes.White, null, Sized(N(0), N(1), N(2), N(3))); break;
                        case "fillText":
                        case "strokeText":
                            var text = Text(a.Count > 0 ? $"{a[0]}" : "", now, key == "fillText" ? now.Fill : now.Stroke);
                            var at = new Point(N(1) - Shift(now.Align, text.Width), N(2) - Drop(now.Baseline, text));
                            if (key == "fillText") Faded(dc, now, () => dc.DrawText(text, at));
                            else Faded(dc, now, () => dc.DrawGeometry(null, new Pen(now.Stroke, now.Line), text.BuildGeometry(at)));
                            break;
                        case "drawImage":
                            if (Picture(a.Count > 0 ? $"{a[0]}" : "") is not { } picture) break;
                            double w = Has(3) ? N(3) : picture.Width;
                            double h = Has(4) ? N(4) : picture.Height;
                            Faded(dc, now, () => dc.DrawImage(picture, Sized(N(1), N(2), w, h)));
                            break;
                    }
                }
                catch (Exception) { }
            }
            for (; now.Pushed > 0; now.Pushed--) dc.Pop();
            while (saved.Count > 0)
                for (now = saved.Pop(); now.Pushed > 0; now.Pushed--) dc.Pop();
        }
        group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, height));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return (image, width, height);
    }

    ////////// HOW EACH IS DRAWN //////////

    // A push that restore takes back.
    static void Push(DrawingContext dc, State now, Action push)
    {
        push();
        now.Pushed++;
    }

    // Drawn with the alpha in force.
    static void Faded(DrawingContext dc, State now, Action draw)
    {
        if (now.Alpha < 1) dc.PushOpacity(now.Alpha);
        draw();
        if (now.Alpha < 1) dc.Pop();
    }

    // A rectangle from x, y, w and h, which may be negative, as a canvas allows.
    static Rect Sized(double x, double y, double w, double h) => new(Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h));

    // A segment added to the figure being drawn, one begun at start first when there is none.
    static void To(PathGeometry path, ref PathFigure? figure, Point start, Action<PathFigure> add)
    {
        if (figure is null)
        {
            figure = new PathFigure { StartPoint = start };
            path.Figures.Add(figure);
        }
        add(figure);
    }

    // A rectangle, rounded by r, as a figure of its own, the way a canvas adds one.
    static void Box(PathGeometry path, ref PathFigure? figure, Rect r, double round)
    {
        path.AddGeometry(new RectangleGeometry(r, round, round));
        figure = null;
    }

    // An arc around x, y from one angle to another, in radians, joined to the figure as a canvas joins it.
    static void Arc(PathGeometry path, ref PathFigure? figure, double x, double y, double r, double from, double to, bool back)
    {
        var start = new Point(x + r * Math.Cos(from), y + r * Math.Sin(from));
        if (figure is null)
        {
            figure = new PathFigure { StartPoint = start };
            path.Figures.Add(figure);
        }
        else figure.Segments.Add(new LineSegment(start, true));
        double sweep = back ? from - to : to - from;
        if (sweep >= 2 * Math.PI || sweep <= -2 * Math.PI)
        {
            var half = new Point(x - r * Math.Cos(from), y - r * Math.Sin(from));
            var way = back ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
            figure.Segments.Add(new ArcSegment(half, new Size(r, r), 0, false, way, true));
            figure.Segments.Add(new ArcSegment(start, new Size(r, r), 0, false, way, true));
            return;
        }
        sweep = ((sweep % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
        var end = new Point(x + r * Math.Cos(to), y + r * Math.Sin(to));
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, sweep > Math.PI, back ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
    }

    // Text in the font in force.
    static FormattedText Text(string text, State now, Brush ink) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, now.Face, Math.Max(1, now.Size), ink, 1.0);

    // How far left of x text starts, for its textAlign.
    static double Shift(string align, double width) => align switch
    {
        "center" => width / 2,
        "right" or "end" => width,
        _ => 0,
    };

    // How far above y its top is, for its textBaseline.
    static double Drop(string baseline, FormattedText text) => baseline switch
    {
        "top" or "hanging" => 0,
        "middle" => text.Height / 2,
        "bottom" or "ideographic" => text.Height,
        _ => text.Baseline,
    };

    ////////// CSS //////////

    // A CSS colour: #rgb, #rgba, #rrggbb, #rrggbbaa, rgb(), rgba() or a name. Black when it isn't one.
    static Brush Paint(string css)
    {
        var c = css.Trim().ToLowerInvariant();
        try
        {
            if (c.StartsWith('#'))
            {
                var hex = c[1..];
                if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
                byte P(int i) => byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber);
                var color = Color.FromArgb(hex.Length == 8 ? P(6) : (byte)255, P(0), P(2), P(4));
                return Frozen(new SolidColorBrush(color));
            }
            if (c.StartsWith("rgb"))
            {
                var parts = c[(c.IndexOf('(') + 1)..c.IndexOf(')')].Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                byte B(int i) => (byte)Compat.Clamp(double.Parse(parts[i].TrimEnd('%'), CultureInfo.InvariantCulture), 0, 255);
                double alpha = parts.Length > 3 ? double.Parse(parts[3].TrimEnd('%'), CultureInfo.InvariantCulture) : 1;
                if (parts.Length > 3 && parts[3].EndsWith('%')) alpha /= 100;
                return Frozen(new SolidColorBrush(Color.FromArgb((byte)Compat.Clamp(alpha * 255, 0, 255), B(0), B(1), B(2))));
            }
            if (c == "transparent") return Brushes.Transparent;
            return Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)));
        }
        catch (Exception) { return Brushes.Black; }
    }

    static Brush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    // A CSS font, like "italic 700 14px Segoe UI, sans-serif": its face and size in pixels. The generic families are
    // Windows' own.
    static (Typeface, double) Font(string css)
    {
        var style = FontStyles.Normal;
        var weight = FontWeights.Normal;
        double size = 10;
        var words = css.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        for (; i < words.Length; i++)
        {
            var w = words[i].ToLowerInvariant();
            if (w is "italic" or "oblique") style = FontStyles.Italic;
            else if (w == "bold") weight = FontWeights.Bold;
            else if (int.TryParse(w, out int n)) weight = FontWeight.FromOpenTypeWeight(Compat.Clamp(n, 1, 999));
            else if (w.EndsWith("px") || w.EndsWith("pt"))
            {
                var number = w.Split('/')[0];
                if (double.TryParse(number[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) size = number.EndsWith("pt") ? s * 96 / 72 : s;
                i++;
                break;
            }
        }
        var families = string.Join(" ", words.Skip(i)).Split(',').Select(f => f.Trim().Trim('"', '\'')).Select(f => f.ToLowerInvariant() switch
        {
            "sans-serif" or "system-ui" or "-apple-system" or "ui-sans-serif" => "Segoe UI",
            "serif" or "ui-serif" => "Georgia",
            "monospace" or "ui-monospace" => "Consolas",
            _ => f,
        }).Where(f => f.Length > 0);
        var family = string.Join(", ", families.Append("Segoe UI"));
        return (new Typeface(new FontFamily(family), style, weight, FontStretches.Normal), size);
    }

    // A picture inside a data:image/ address, read once, or null when it isn't one.
    static BitmapSource? Picture(string src)
    {
        if (!src.StartsWith("data:image/")) return null;
        if (pictures.Count > 300) pictures.Clear();
        return pictures.GetOrAdd(src, s =>
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(Convert.FromBase64String(s[(s.IndexOf(',') + 1)..]));
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception) { return null; }
        });
    }
}
