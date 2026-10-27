// OnsayView.cs - one Onsay, drawn natively from the numbers in Face.cs. The frame loop only runs
// while something is moving, blinking and fidgeting are timers, and breathing is a WPF animation.
//
// 1.  Frame()                   steps gestures, residuals and springs, and sleeps when he settles
// 2.  OnRender(dc)              body, surface, face, mouth, and the X on his back
// 3.  Schedule()                blink and wander timers for the current mood
// 4.  Breathe()                 breathing at the current mood's rate
// 5.  Play(name, done, setup)   starts a gesture. done runs when it ends
// 6.  Blink()
// 7.  Express(name)             a pose. Channels it does not name keep their value
// 8.  React(name)               an event from Reactions: expression, gesture, then a mood
// 9.  Mood(name)                what he does when nothing is happening
// 10. Watch(point)              look at a point relative to his centre in DIPs, or null for front
// 11. Carry(velocity)           being dragged, in DIPs a second, or null when put down
// 12. Turn(away)                turns round so the X on his back faces you
// 13. Speak(text, onText)       mouths text a character at a time, onText gets the text so far
// 14. Hush()                    stops speaking and rests his mouth
//     Paint(look)               how every one of him looks: colour, face and surface
//     Project, SurfaceTransform, MouthArc, Smooth   the sphere and the mouth, see section 2
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Onsay.Character;

public sealed class OnsayView : FrameworkElement
{
    ////////// LOOK //////////

    /* How he looks, one look for every one of him on screen: his body colour, his face colour, and his surface, a name in
       Materials. Paint(look) changes it, and every one of him repaints. The same names as the website's character. */
    public sealed record Appearance(string Color = Desktop.ChatWindow.Brand, string Face = "#FFFFFF", string Material = "soft");

    // Each surface is how strong the depth toward his edge and the gloss on top are. The highlight follows the gloss.
    public static readonly Dictionary<string, (double depth, double gloss)> Materials = new()
    {
        ["soft"] = (0.4, 0.32), ["flat"] = (0, 0), ["matte"] = (0.55, 0.12), ["glossy"] = (0.5, 0.7), ["liquid"] = (0.58, 1),
    };

    static readonly Brush[] Layers =
    {
        Radial(0.42, 0.36, 0.72, (0.34, Color.FromArgb(0, 0, 0, 0)), (1, Color.FromArgb(140, 0, 0, 0))),             // depth
        Radial(0.32, 0.26, 0.5, (0, Color.FromArgb(140, 255, 255, 255)), (1, Color.FromArgb(0, 255, 255, 255))),     // gloss
        Radial(0.3, 0.23, 0.17, (0, Color.FromArgb(242, 255, 255, 255)), (1, Color.FromArgb(0, 255, 255, 255))),     // highlight
    };
    static readonly Geometry BallClip = Frozen(new EllipseGeometry(new Point(0, 0), Face.R, Face.R));
    static Brush BodyBrush = null!, FaceBrush = null!;
    static Pen SlitPen = null!, ArcPen = null!, BackPen = null!;
    static double[] Strength = null!;
    static event Action? Repaint;
    public static Appearance Current { get; private set; } = new();
    static OnsayView() => Paint(new Appearance());

    // Every one of him takes the new look at once. A colour that doesn't read keeps the default.
    public static void Paint(Appearance look)
    {
        static Color? Read(string hex) { try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return null; } }
        var (depth, gloss) = Materials.GetValueOrDefault(look.Material, Materials["soft"]);
        BodyBrush = Frozen(new SolidColorBrush(Read(look.Color) ?? Desktop.ChatWindow.Accent.Color));
        FaceBrush = Frozen(new SolidColorBrush(Read(look.Face) ?? Colors.White));
        SlitPen = Stroke(Face.MouthT * 0.62, PenLineCap.Flat);
        ArcPen = Stroke(Face.MouthT * 0.8, PenLineCap.Flat);
        BackPen = Stroke(Face.MouthT * 0.9, PenLineCap.Round);
        Strength = new[] { depth, gloss, Math.Pow(gloss, 2.2) };
        Current = look;
        Repaint?.Invoke();
    }

    ////////// STATE //////////

    readonly Spring[] ch = new Spring[Channels.Count];
    readonly Spring?[] residual = new Spring?[Channels.Count];
    readonly double[] add = new double[Channels.Count];
    readonly List<GestureRun> gestures = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Random rng = new();
    readonly ScaleTransform breathScale = new();
    readonly TranslateTransform breathMove = new();

    Dictionary<Ch, double> expressionTargets = new(Expressions.All["happy"]);
    string mood = "calm";
    int era, speech;          // bumped to cancel an older timer chain or sentence
    DispatcherTimer? blinkTimer, wanderTimer;
    bool running, paused, carried;
    double last;
    Point? point;

    // Windows' "show animations" setting. Off, he still reacts but skips the journey.
    static bool Reduced => !SystemParameters.ClientAreaAnimation;

    public OnsayView()
    {
        for (int i = 0; i < ch.Length; i++) ch[i] = new Spring(Channels.Rest((Ch)i), Channels.Spring((Ch)i));
        foreach (var (k, v) in expressionTargets) ch[(int)k].Set(v);
        RenderTransform = new TransformGroup { Children = { breathScale, breathMove } };
        RenderTransformOrigin = new Point(0.5, 0.5);
        Loaded += (_, _) => { Repaint += InvalidateVisual; Mood(mood); };
        Unloaded += (_, _) => { Repaint -= InvalidateVisual; Sleep(); era++; speech++; };
        SizeChanged += (_, _) => Breathe();
    }

    // Paused while nobody can see him. Timers keep their chain but do nothing.
    public bool Paused
    {
        get => paused;
        set { paused = value; if (!value) Wake(); }
    }

    ////////// 1. FRAME //////////

    void Wake()
    {
        if (running || paused) return;
        running = true;
        last = 0;
        CompositionTarget.Rendering += Frame;
    }

    void Sleep()
    {
        if (!running) return;
        running = false;
        CompositionTarget.Rendering -= Frame;
    }

    void Frame(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalMilliseconds;
        double dt = last > 0 ? (now - last) / 1000 : 0.016;
        last = now;
        if (paused) { Sleep(); return; }

        bool busy = false;
        Aim();
        Array.Clear(add);
        for (int i = gestures.Count - 1; i >= 0; i--)
        {
            var g = gestures[i];
            double p = (now - g.Start) / g.Def.Ms;
            if (p < 1) { g.Def.At(p, g, add); busy = true; continue; }
            gestures.RemoveAt(i);
            // Whatever it still held becomes a residual that springs home, so nothing teleports.
            var held = new double[Channels.Count];
            g.Def.At(1, g, held);
            for (int c = 0; c < held.Length; c++)
            {
                double v = Channels.Wrap((Ch)c, held[c]);
                if (Math.Abs(v) < 0.001) continue;
                var r = residual[c] ??= new Spring(0, Channels.Settle);
                r.V += v;
                r.Target = 0;
            }
            g.Done?.Invoke();
        }
        for (int i = 0; i < residual.Length; i++)
        {
            if (residual[i] is not { } r) continue;
            if (r.Step(dt)) busy = true;
            add[i] += r.V;
        }
        foreach (var s in ch) if (s.Step(dt)) busy = true;

        InvalidateVisual();
        if (!busy) Sleep();
    }

    // Looking follows the watched point, unless carried or the pose says where to look (thinking).
    void Aim()
    {
        if (carried || expressionTargets.ContainsKey(Ch.LookX)) return;
        double over = Math.Max(140, ActualWidth * 2.2);
        Look(point is { } p ? p.X / over : 0, point is { } q ? q.Y / over : 0);
    }

    void Look(double x, double y, double tilt = double.NaN)
    {
        ch[(int)Ch.LookX].Target = Math.Clamp(x, -1, 1);
        ch[(int)Ch.LookY].Target = Math.Clamp(y, -1, 1);
        if (!double.IsNaN(tilt)) ch[(int)Ch.Tilt].Target = tilt;
    }

    ////////// 2. DRAWING //////////

    double V(Ch c) => ch[(int)c].V + add[(int)c];
    static double Unit(double v) => Math.Clamp(v, 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        double scale = Math.Max(0.05, V(Ch.Scale)), sq = V(Ch.Squash);
        double about = Unit(V(Ch.About));
        double lookX = V(Ch.LookX) * (1 - about);   // looking and leaning fade as he turns away
        double spun = V(Ch.Tilt) + lookX * Face.TiltMax;

        var m = Matrix.Identity;
        m.Scale(scale * (1 + sq * 0.5), scale * (1 - sq * 0.5));
        m.Rotate(spun);
        m.Translate(V(Ch.X), V(Ch.Y));
        m.Scale(size / 100, size / 100);
        m.Translate(ActualWidth / 2, ActualHeight / 2);
        dc.PushTransform(new MatrixTransform(m));

        dc.DrawEllipse(BodyBrush, null, default, Face.R, Face.R);
        // The light does not lean with him: the surface is turned back by what his body turned by.
        dc.PushTransform(new RotateTransform(-spun));
        for (int i = 0; i < Layers.Length; i++)
        {
            if (Strength[i] <= 0) continue;
            dc.PushOpacity(Strength[i]);
            dc.DrawEllipse(Layers[i], null, default, Face.R, Face.R);
            dc.Pop();
        }
        dc.Pop();

        // Looking and turning round are both the ball rotating. The turn is unclamped so it overshoots.
        double yaw = lookX * Face.LookYaw + V(Ch.Spin) + V(Ch.About) * Math.PI;
        double pitch = -V(Ch.LookY) * (1 - about) * Face.LookPitch;

        dc.PushClip(BallClip);
        DrawEye(dc, -Face.EyeX, Ch.EyeArcL, Ch.EyeScaleL, Ch.LidL, Ch.LowerL, yaw, pitch);
        DrawEye(dc, Face.EyeX, Ch.EyeArcR, Ch.EyeScaleR, Ch.LidR, Ch.LowerR, yaw, pitch);
        DrawMouth(dc, yaw, pitch);
        // The X sits on the far side of the ball, drawn with the same projection half a turn further round.
        double behind = yaw + Math.PI;
        if (about > 0.02 && Project(0, 0, behind, pitch).z > 0.05)
        {
            Point at(double px, double py) { var p = Project(px, py, behind, pitch); return new Point(p.x, p.y); }
            double s = Face.BackX;
            dc.PushOpacity(Math.Min(1, about * 1.5));
            dc.DrawLine(BackPen, at(-s, -s), at(s, s));
            dc.DrawLine(BackPen, at(s, -s), at(-s, s));
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }

    // An eye is a whole shape behind a lid window, because a blink covers the eye rather than shrinking it.
    void DrawEye(DrawingContext dc, double px, Ch arcCh, Ch scaleCh, Ch lidCh, Ch lowerCh, double yaw, double pitch)
    {
        var at = Project(px, Face.EyeY, yaw, pitch);
        if (at.z <= 0.03) return;

        double arc = Unit(V(arcCh)), lid = Unit(V(lidCh)), lower = Unit(V(lowerCh));
        double rEye = Face.EyeR * Math.Max(0.2, V(scaleCh));
        dc.PushTransform(new MatrixTransform(SurfaceTransform(at)));

        double top = -rEye + lid * 2 * rEye, bottom = rEye - lower * 2 * rEye;
        dc.PushClip(new RectangleGeometry(new Rect(-rEye * 1.6, top, rEye * 3.2, Math.Max(0, bottom - top))));
        dc.PushOpacity(1 - arc);
        dc.DrawEllipse(FaceBrush, null, default, rEye, rEye);
        dc.Pop();
        dc.Pop();

        // The lid line fades in over the last quarter of a close, coming down the way the lid travels.
        double shutting = Math.Max(0, (lid - 0.72) / 0.28);
        double lineOpacity = Math.Min(1, shutting) * Unit((0.62 - arc) / 0.24);
        if (lineOpacity > 0)
        {
            double y = -rEye * 0.34 * (1 - shutting), halfW = rEye * 0.84;
            dc.PushOpacity(lineOpacity);
            dc.DrawLine(SlitPen, new Point(-halfW, y), new Point(halfW, y));
            dc.Pop();
        }

        if (arc > 0.01)
        {
            double w = rEye * 1.15;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(-w, 2.6), false, false);
                c.QuadraticBezierTo(new Point(0, -7.4), new Point(w, 2.6), true, false);
            }
            dc.PushOpacity(arc);
            dc.DrawGeometry(null, ArcPen, g);
            dc.Pop();
        }
        dc.Pop();
    }

    // One filled shape: part of a circle, projected point by point, that swells in the middle as it opens.
    void DrawMouth(DrawingContext dc, double yaw, double pitch)
    {
        double w = Face.MouthW * Math.Max(0.15, V(Ch.MouthWidth));
        double open = Unit(V(Ch.MouthOpen));
        var (outer, inner) = MouthArc(w, V(Ch.MouthCurve) * Face.MouthSag, Face.MouthY, 16, t => Face.MouthT + open * 17 * (1 - t * t));
        var front = new List<Point>(outer.Count);
        var back = new List<Point>(inner.Count);
        for (int i = 0; i < outer.Count; i++)
        {
            var a = Project(outer[i].X, outer[i].Y, yaw, pitch);
            var b = Project(inner[i].X, inner[i].Y, yaw, pitch);
            if (a.z > 0.03 && b.z > 0.03) { front.Add(new Point(a.x, a.y)); back.Add(new Point(b.x, b.y)); }
            else if (front.Count > 0) break;   // gone round the side
        }
        if (front.Count < 2) return;
        back.Reverse();
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            Smooth(c, front, true);
            Smooth(c, back, false);
        }
        dc.DrawGeometry(FaceBrush, null, g);
    }

    // A point painted on the sphere, turned by yaw and pitch. z is whether it faces us and how foreshortened.
    static (double x, double y, double z) Project(double px, double py, double yaw, double pitch)
    {
        double x0 = px / Face.R, y0 = py / Face.R;
        double z0 = Math.Sqrt(Math.Max(0.0001, 1 - x0 * x0 - y0 * y0));
        double x1 = x0 * Math.Cos(yaw) + z0 * Math.Sin(yaw);
        double z1 = z0 * Math.Cos(yaw) - x0 * Math.Sin(yaw);
        double y2 = y0 * Math.Cos(pitch) - z1 * Math.Sin(pitch);
        double z2 = z1 * Math.Cos(pitch) + y0 * Math.Sin(pitch);
        return (x1 * Face.R, y2 * Face.R, z2);
    }

    // Squashes a shape on the surface toward the edge, only in the direction it leans away.
    static Matrix SurfaceTransform((double x, double y, double z) p)
    {
        double a = Math.Atan2(p.y, p.x) * 180 / Math.PI;
        var m = Matrix.Identity;
        m.Rotate(-a);
        m.Scale(Math.Max(0.02, p.z), 1);
        m.Rotate(a);
        m.Translate(p.x, p.y);
        return m;
    }

    // w is half the chord, sag how deep it bulges. The circle through them has radius (w² + sag²) / 2·sag.
    static (List<Point> outer, List<Point> inner) MouthArc(double w, double sag, double my, int steps, Func<double, double> thickness)
    {
        var outer = new List<Point>(steps + 1);
        var inner = new List<Point>(steps + 1);
        double s = Math.Abs(sag), flip = sag >= 0 ? 1 : -1;
        bool flat = s < 0.4;
        double r = flat ? 0 : (w * w + s * s) / (2 * s);
        double yc = flat ? 0 : -(r - s);
        double from = flat ? 0 : Math.Atan2(-yc, -w), to = flat ? 0 : Math.Atan2(-yc, w);
        for (int i = 0; i <= steps; i++)
        {
            double f = (double)i / steps, half = thickness(f * 2 - 1) / 2;
            double th = from + (to - from) * f;
            double px = flat ? -w + 2 * w * f : r * Math.Cos(th), py = flat ? 0 : yc + r * Math.Sin(th);
            double nx = flat ? 0 : Math.Cos(th), ny = flat ? 1 : Math.Sin(th);
            outer.Add(new Point(px + nx * half, my + flip * (py + ny * half)));
            inner.Add(new Point(px - nx * half, my + flip * (py - ny * half)));
        }
        return (outer, inner);
    }

    // A quadratic through each midpoint. Joins onto the figure so far unless it starts one.
    static void Smooth(StreamGeometryContext c, List<Point> pts, bool start)
    {
        if (start) c.BeginFigure(pts[0], true, true);
        else c.LineTo(pts[0], false, false);
        for (int i = 1; i < pts.Count - 1; i++)
            c.QuadraticBezierTo(pts[i], new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2), false, false);
        c.LineTo(pts[^1], false, false);
    }

    ////////// 3-5. ON HIS OWN //////////

    double Rand((double lo, double hi) r) => r.lo + rng.NextDouble() * (r.hi - r.lo);

    // A repeating timer chain that stops itself once the era it belongs to has passed.
    void Every((double lo, double hi) gap, int mine, Action tick, Action<DispatcherTimer> keep)
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(Rand(gap)) };
        t.Tick += (_, _) =>
        {
            if (mine != era) { t.Stop(); return; }
            if (!paused) tick();
            t.Interval = TimeSpan.FromMilliseconds(Rand(gap));
        };
        t.Start();
        keep(t);
    }

    // 3.
    void Schedule()
    {
        blinkTimer?.Stop();
        wanderTimer?.Stop();
        int mine = ++era;
        if (Reduced) return;
        var m = Moods.All[mood];
        Every(m.Blink, mine, Blink, t => blinkTimer = t);
        Every(m.Wander, mine, () => { if (gestures.Count == 0) Play(m.WanderWith[rng.Next(m.WanderWith.Length)]); }, t => wanderTimer = t);
    }

    // 4.
    void Breathe()
    {
        double breath = Reduced ? 0 : Moods.All[mood].Breath;
        AnimationTimeline? cycle(double to)
        {
            if (breath <= 0 || ActualHeight <= 0) return null;
            var a = new DoubleAnimation(to, TimeSpan.FromSeconds(1.7 / breath))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(a, 30);
            return a;
        }
        breathMove.BeginAnimation(TranslateTransform.YProperty, cycle(-1.6 * ActualHeight / 100));
        breathScale.BeginAnimation(ScaleTransform.ScaleYProperty, cycle(1.018));
    }

    // 5. Reduced motion still gets the end of the story, just not the journey.
    void Play(string name, Action? done = null, Action<GestureRun>? setup = null)
    {
        if (Reduced || !Gestures.All.TryGetValue(name, out var def)) { done?.Invoke(); return; }
        var run = new GestureRun { Def = def, Start = clock.Elapsed.TotalMilliseconds, Done = done };
        setup?.Invoke(run);
        gestures.Add(run);
        Wake();
    }

    ////////// 6-14. WHAT HE DOES //////////

    // 6. Measures what each eye has left to close, so a squint blinks shut rather than past it.
    void Blink()
    {
        if (gestures.Any(g => g.Def == Gestures.All["blink"])) return;
        Play("blink", null, g =>
        {
            g.ReachL = Math.Max(0, 1.02 - ch[(int)Ch.LidL].Target);
            g.ReachR = Math.Max(0, 1.02 - ch[(int)Ch.LidR].Target);
            g.DropL = ch[(int)Ch.LowerL].Target;
            g.DropR = ch[(int)Ch.LowerR].Target;
        });
    }

    // 7.
    public void Express(string name)
    {
        if (!Expressions.All.TryGetValue(name, out var pose)) return;
        expressionTargets = new(pose);
        foreach (var (k, v) in expressionTargets) ch[(int)k].Target = v;
        Wake();
    }

    // 8.
    public void React(string name)
    {
        if (!Reactions.All.TryGetValue(name, out var r)) return;
        if (r.Expression is { } e) Express(e);
        if (r.Mood is { } m) Mood(m);
        Play(r.Gesture ?? "", () => { if (r.Then is { } t) Mood(t); });
    }

    // 9.
    public void Mood(string name)
    {
        if (!Moods.All.TryGetValue(name, out var m)) return;
        mood = name;
        Express(m.Expression);
        Schedule();
        Breathe();
    }

    // 10.
    public void Watch(Point? relative)
    {
        point = relative;
        Wake();
    }

    // 11. He looks and leans the way he is taken, a beat behind the hand because the springs lag.
    public void Carry(Vector? velocity)
    {
        carried = velocity is not null;
        var v = (velocity ?? default) / 1200;
        Look(v.X, v.Y * 0.6, Math.Clamp(v.X, -1, 1) * 9);
        Wake();
    }

    // 12.
    public void Turn(bool away)
    {
        ch[(int)Ch.About].Target = away ? 1 : 0;
        Wake();
    }

    // 13. Each character sets the mouth from its viseme, with a little of his resting smile kept in.
    public async Task Speak(string text, Action<string>? onText = null, double cps = 46)
    {
        int mine = ++speech;
        for (int i = 0; i < text.Length && mine == speech; i++)
        {
            var shape = Visemes.For(i > 0 ? text[i - 1] : ' ', text[i], i + 1 < text.Length ? text[i + 1] : ' ');
            ch[(int)Ch.MouthOpen].Target = shape.Open * (0.85 + rng.NextDouble() * 0.3);
            ch[(int)Ch.MouthWidth].Target = shape.Width;
            ch[(int)Ch.MouthCurve].Target = expressionTargets.GetValueOrDefault(Ch.MouthCurve, 0.85) * 0.4 + shape.Curve * 0.6;
            Wake();
            onText?.Invoke(text[..(i + 1)]);
            await Task.Delay(TimeSpan.FromMilliseconds(1000 / cps + shape.Pause));
        }
        if (mine == speech) Hush();
    }

    // 14.
    public void Hush()
    {
        speech++;
        ch[(int)Ch.MouthOpen].Target = expressionTargets.GetValueOrDefault(Ch.MouthOpen, 0);
        ch[(int)Ch.MouthWidth].Target = expressionTargets.GetValueOrDefault(Ch.MouthWidth, 1);
        ch[(int)Ch.MouthCurve].Target = expressionTargets.GetValueOrDefault(Ch.MouthCurve, 0.85);
        Wake();
    }

    ////////// HELPERS //////////

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    static Pen Stroke(double width, PenLineCap cap) =>
        Frozen(new Pen(FaceBrush, width) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round });

    static Brush Radial(double cx, double cy, double r, params (double offset, Color color)[] stops)
    {
        var b = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = r, RadiusY = r };
        foreach (var (offset, color) in stops) b.GradientStops.Add(new GradientStop(color, offset));
        return Frozen(b);
    }
}
