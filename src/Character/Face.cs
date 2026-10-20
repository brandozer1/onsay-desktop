// Face.cs - Onsay the character as data, ported from the original, ../onsay/website/src/lib/character.js, with the same numbers,
// so he moves on the desktop exactly the way he moves on the web.
//
// 1. Face          geometry, in viewBox units on a 100 wide box centred at 0
// 2. Ch, Channels  every animated value: its rest value, its spring, and Wrap for the cyclic ones
// 3. Spring        the spring behind one channel
// 4. Expressions   named poses, each a partial set of channel targets
// 5. Gestures      one off movements, written as a function of their own progress 0 to 1
// 6. Moods         what he does when nothing is happening
// 7. Reactions     an event, mapped to an expression, a gesture and the mood after it
// 8. Visemes       mouth shapes for speech, and For(prev, c, next) to pick one

namespace Onsay.Character;

////////// 1. FACE //////////

static class Face
{
    public const double R = 46, EyeX = 15.5, EyeY = -8, EyeR = 8.4;
    public const double MouthY = 12, MouthW = 21, MouthT = 7.4, MouthSag = 13;
    public const double LookYaw = 0.5, LookPitch = 0.34;   // radians he turns at full look
    public const double TiltMax = 7;                       // degrees he leans toward what he watches
    public const double BackX = 11;                        // half the span of the X on his back
}

////////// 2. CHANNELS //////////

// Lids are two numbers each, so a blink, squint, wink and sleepy eye are one control at different values.
public enum Ch
{
    LookX, LookY,          // -1 to 1, where he is looking
    Spin,                  // radians of extra yaw
    LidL, LidR,            // upper lid, 0 open 1 shut
    LowerL, LowerR,        // lower lid, the one that reads as doubt
    EyeScaleL, EyeScaleR,
    EyeArcL, EyeArcR,      // 0 round, 1 happy arc
    MouthCurve,            // -1 frown, 0 line, 1 smile
    MouthWidth,
    MouthOpen,
    FaceShow,              // 0 a plain circle
    About,                 // 0 facing you, 1 turned round showing the X
    Tilt,                  // degrees
    Scale,
    Squash,                // positive wide and short
    X, Y,
}

static class Channels
{
    public static readonly int Count = Enum.GetValues<Ch>().Length;

    public static double Rest(Ch c) => c switch
    {
        Ch.EyeScaleL or Ch.EyeScaleR or Ch.MouthWidth or Ch.FaceShow or Ch.Scale => 1,
        Ch.MouthCurve => 0.85,
        _ => 0,
    };

    public static (double k, double d) Spring(Ch c) => c switch
    {
        Ch.LookX or Ch.LookY => (90, 17),
        Ch.LidL or Ch.LidR => (950, 56),
        Ch.FaceShow => (150, 22),
        Ch.About => (120, 17),
        Ch.MouthOpen => (520, 40),
        Ch.MouthWidth => (430, 37),
        Ch.MouthCurve => (360, 34),
        Ch.LowerL or Ch.LowerR => (300, 30),
        Ch.Scale => (260, 22),
        Ch.Squash => (240, 19),
        Ch.X or Ch.Y or Ch.Tilt => (150, 20),
        _ => (190, 26),
    };

    // Walks home whatever a gesture was still holding when it ended.
    public static readonly (double k, double d) Settle = (58, 14);

    // A whole turn is no turn, so a finished spin settles to nothing instead of unspinning.
    public static double Wrap(Ch c, double v)
    {
        double period = c switch { Ch.Spin => Math.PI * 2, Ch.Tilt => 360, _ => 0 };
        if (period == 0) return v;
        double half = period / 2;
        return ((((v + half) % period) + period) % period) - half;
    }

    public static void Add(this double[] a, Ch c, double v) => a[(int)c] += v;
}

////////// 3. SPRING //////////

public sealed class Spring
{
    public double V, Target, Vel;
    readonly double k, d;

    public Spring(double value, (double k, double d) cfg)
    {
        V = Target = value;
        (k, d) = cfg;
    }

    // Returns whether it is still moving. dt is capped so waking from a long sleep is not a launch.
    public bool Step(double dt)
    {
        double h = Math.Min(dt, 0.032);
        Vel += (-k * (V - Target) - d * Vel) * h;
        V += Vel * h;
        return Math.Abs(Vel) > 0.0008 || Math.Abs(V - Target) > 0.0008;
    }

    public void Set(double v) { V = Target = v; Vel = 0; }
}

////////// 4. EXPRESSIONS //////////

static class Expressions
{
    static Dictionary<Ch, double> P(params (Ch, double)[] p) => p.ToDictionary(x => x.Item1, x => x.Item2);

    public static readonly Dictionary<string, Dictionary<Ch, double>> All = new()
    {
        ["happy"] = P((Ch.LidL, 0), (Ch.LidR, 0), (Ch.LowerL, 0), (Ch.LowerR, 0), (Ch.EyeScaleL, 1), (Ch.EyeScaleR, 1), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0.95), (Ch.MouthWidth, 1), (Ch.MouthOpen, 0)),
        ["grin"] = P((Ch.LidL, 0), (Ch.LidR, 0), (Ch.LowerL, 0), (Ch.LowerR, 0), (Ch.EyeArcL, 1), (Ch.EyeArcR, 1), (Ch.MouthCurve, 1), (Ch.MouthWidth, 1.1), (Ch.MouthOpen, 0.22)),
        ["neutral"] = P((Ch.LidL, 0), (Ch.LidR, 0), (Ch.LowerL, 0), (Ch.LowerR, 0), (Ch.EyeScaleL, 1), (Ch.EyeScaleR, 1), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0.18), (Ch.MouthWidth, 0.8), (Ch.MouthOpen, 0)),
        ["wink"] = P((Ch.LidL, 0), (Ch.LidR, 1), (Ch.LowerL, 0), (Ch.LowerR, 0), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0.95), (Ch.MouthWidth, 1), (Ch.MouthOpen, 0)),
        ["surprised"] = P((Ch.LidL, 0), (Ch.LidR, 0), (Ch.LowerL, 0), (Ch.LowerR, 0), (Ch.EyeScaleL, 1.28), (Ch.EyeScaleR, 1.28), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0), (Ch.MouthWidth, 0.62), (Ch.MouthOpen, 1)),
        ["thinking"] = P((Ch.LidL, 0.3), (Ch.LidR, 0.3), (Ch.LowerL, 0.16), (Ch.LowerR, 0.16), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, -0.12), (Ch.MouthWidth, 0.55), (Ch.MouthOpen, 0), (Ch.LookX, -0.85), (Ch.LookY, -0.5)),
        ["suspicious"] = P((Ch.LidL, 0.44), (Ch.LidR, 0.18), (Ch.LowerL, 0.32), (Ch.LowerR, 0.32), (Ch.EyeScaleL, 1), (Ch.EyeScaleR, 1), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, -0.25), (Ch.MouthWidth, 0.52), (Ch.MouthOpen, 0), (Ch.LookX, -0.4)),
        ["sad"] = P((Ch.LidL, 0.34), (Ch.LidR, 0.34), (Ch.LowerL, 0), (Ch.LowerR, 0), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, -0.8), (Ch.MouthWidth, 0.8), (Ch.MouthOpen, 0), (Ch.LookY, 0.32)),
        ["dizzy"] = P((Ch.LidL, 0.16), (Ch.LidR, 0.2), (Ch.LowerL, 0.12), (Ch.LowerR, 0.08), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.EyeScaleL, 1.12), (Ch.EyeScaleR, 1.12), (Ch.MouthCurve, -0.15), (Ch.MouthWidth, 0.72), (Ch.MouthOpen, 0.55)),
        ["sleepy"] = P((Ch.LidL, 0.76), (Ch.LidR, 0.76), (Ch.LowerL, 0.06), (Ch.LowerR, 0.06), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0.25), (Ch.MouthWidth, 0.5), (Ch.MouthOpen, 0.05), (Ch.LookY, 0.22)),
        ["focused"] = P((Ch.LidL, 0.26), (Ch.LidR, 0.26), (Ch.LowerL, 0.24), (Ch.LowerR, 0.24), (Ch.EyeArcL, 0), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0.1), (Ch.MouthWidth, 0.62), (Ch.MouthOpen, 0)),
        ["cheeky"] = P((Ch.LidL, 0), (Ch.LidR, 0.32), (Ch.LowerL, 0), (Ch.LowerR, 0.26), (Ch.EyeArcL, 1), (Ch.EyeArcR, 0), (Ch.MouthCurve, 0.8), (Ch.MouthWidth, 0.9), (Ch.MouthOpen, 0.12)),
    };
}

////////// 5. GESTURES //////////

public sealed class GestureDef
{
    public required double Ms;
    public required Action<double, GestureRun, double[]> At;   // progress, the run, offsets to add to
}

public sealed class GestureRun
{
    public required GestureDef Def;
    public required double Start;
    public Action? Done;
    public double ReachL = 1.02, ReachR = 1.02, DropL, DropR;   // blink only: how far each eye has left
}

static class Gestures
{
    // Eases on every oscillation, so he leaves stillness and arrives back in it without a jolt.
    static double Sstep(double p) => p * p * (3 - 2 * p);
    static double Rise(double p, double t = 0.22) => p < t ? Sstep(p / t) : 1;
    static double Settle(double p, double a = 0.24, double b = 0.28) => Rise(p, a) * (p > 1 - b ? Sstep((1 - p) / b) : 1);
    static double Ease(double p) => p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;
    const double PI = Math.PI;

    public static readonly Dictionary<string, GestureDef> All = new()
    {
        ["bob"] = new() { Ms = 2600, At = (p, _, a) => { a.Add(Ch.Y, Math.Sin(p * PI * 2) * 1.9 * Settle(p)); a.Add(Ch.Squash, Math.Sin(p * PI * 2) * 0.035 * Settle(p)); } },
        ["hop"] = new() { Ms = 620, At = (p, _, a) =>
        {
            double lift = Math.Sin(Math.Min(1, p * 1.15) * PI);
            double anticip = p < 0.18 ? Math.Sin(p / 0.18 * PI) : 0;
            double land = p > 0.86 ? Math.Sin((p - 0.86) / 0.14 * PI) : 0;
            a.Add(Ch.Y, -lift * 15);
            a.Add(Ch.Squash, anticip * 0.3 + land * 0.34 - lift * 0.16);
        } },
        ["spin"] = new() { Ms = 950, At = (p, _, a) => a.Add(Ch.Spin, Ease(p) * PI * 2) },
        ["spinBack"] = new() { Ms = 950, At = (p, _, a) => a.Add(Ch.Spin, -Ease(p) * PI * 2) },
        ["roll"] = new() { Ms = 1100, At = (p, _, a) => { a.Add(Ch.X, Ease(p) * 34); a.Add(Ch.Tilt, -Ease(p) * 360); a.Add(Ch.Spin, Ease(p) * PI * 2); } },
        ["shake"] = new() { Ms = 520, At = (p, _, a) => { a.Add(Ch.X, Math.Sin(p * PI * 6) * (1 - p) * 6.5 * Rise(p, 0.14)); a.Add(Ch.Tilt, Math.Sin(p * PI * 6) * (1 - p) * 5 * Rise(p, 0.14)); } },
        ["nod"] = new() { Ms = 620, At = (p, _, a) => { a.Add(Ch.LookY, Math.Sin(p * PI * 4) * (1 - p) * 0.55 * Rise(p, 0.18)); a.Add(Ch.Y, Math.Sin(p * PI * 4) * (1 - p) * 3 * Rise(p, 0.18)); } },
        ["wobble"] = new() { Ms = 900, At = (p, _, a) => a.Add(Ch.Tilt, Math.Sin(p * PI * 4) * (1 - p) * 13 * Rise(p, 0.2)) },
        ["pop"] = new() { Ms = 420, At = (p, _, a) => { a.Add(Ch.Scale, Math.Sin(p * PI) * 0.16); a.Add(Ch.Squash, -Math.Sin(p * PI) * 0.12); } },
        ["land"] = new() { Ms = 560, At = (p, _, a) =>
        {
            double b = Math.Sin(p * PI * 2) * Math.Sin(p * PI);
            a.Add(Ch.Squash, b * 0.32);
            a.Add(Ch.Y, Math.Max(0, b) * 2.2);
        } },
        ["glance"] = new() { Ms = 1800, At = (p, _, a) => { a.Add(Ch.LookX, Math.Sin(p * PI * 2) * 0.8 * Settle(p)); a.Add(Ch.LookY, Math.Sin(p * PI) * -0.25 * Settle(p)); } },
        ["peek"] = new() { Ms = 1500, At = (p, _, a) => a.Add(Ch.Spin, Math.Sin(p * PI) * 1.2 * Settle(p, 0.3, 0.3)) },
        // An offset on the lids, closing faster than it opens. The lower lid lets go as the upper comes down.
        ["blink"] = new() { Ms = 260, At = (p, g, a) =>
        {
            double k = p < 0.34 ? p / 0.34 : 1 - (p - 0.34) / 0.66;
            double e = k * k * (3 - 2 * k);
            a.Add(Ch.LidL, e * g.ReachL); a.Add(Ch.LidR, e * g.ReachR);
            a.Add(Ch.LowerL, -e * g.DropL); a.Add(Ch.LowerR, -e * g.DropR);
        } },
        ["dizzy"] = new() { Ms = 1700, At = (p, _, a) =>
        {
            double fade = 1 - p;
            a.Add(Ch.Spin, Math.Sin(p * PI * 4) * 0.5 * Settle(p, 0.15, 0.35));
            a.Add(Ch.Tilt, Math.Sin(p * PI * 3) * 11 * fade * Rise(p, 0.12));
            a.Add(Ch.LookX, Math.Cos(p * PI * 6) * 0.6 * fade);
            a.Add(Ch.LookY, Math.Sin(p * PI * 6) * 0.45 * fade);
            a.Add(Ch.Y, Math.Sin(p * PI * 2) * 2.4 * Settle(p));
        } },
        ["backflip"] = new() { Ms = 980, At = (p, _, a) =>
        {
            double lift = Math.Sin(Math.Min(1, p * 1.08) * PI);
            double crouch = p < 0.16 ? Math.Sin(p / 0.16 * PI) : 0;
            double land = p > 0.88 ? Math.Sin((p - 0.88) / 0.12 * PI) : 0;
            a.Add(Ch.Y, -lift * 34 + crouch * 4);
            a.Add(Ch.Tilt, -Ease(p) * 360);
            a.Add(Ch.Squash, crouch * 0.26 + land * 0.3 - lift * 0.14);
        } },
        ["giggle"] = new() { Ms = 1100, At = (p, _, a) =>
        {
            double fade = 1 - p * 0.7;
            double bounce = Math.Abs(Math.Sin(p * PI * 5));
            double s = Settle(p, 0.1, 0.22);
            a.Add(Ch.Y, -bounce * 4.6 * fade * s);
            a.Add(Ch.Tilt, Math.Sin(p * PI * 7) * 5 * fade * s);
            a.Add(Ch.Squash, bounce * 0.1 * fade * s);
        } },
    };
}

////////// 6. MOODS //////////

public sealed record Mood(string Expression, double Breath, (double lo, double hi)? Blink, (double lo, double hi)? Wander, string[] WanderWith);

static class Moods
{
    public static readonly Dictionary<string, Mood> All = new()
    {
        ["calm"] = new("happy", 1, (2600, 6500), (7000, 15000), new[] { "glance", "wobble" }),
        ["alert"] = new("neutral", 1.25, (1800, 4200), (4000, 9000), new[] { "glance", "nod", "pop" }),
        ["happy"] = new("grin", 1.4, (2200, 5200), (3500, 8000), new[] { "hop", "wobble", "spin" }),
        ["working"] = new("focused", 0.9, (2200, 5000), (5000, 11000), new[] { "glance", "peek" }),
        ["sleepy"] = new("sleepy", 0.55, (4200, 9000), (12000, 22000), new[] { "wobble" }),
        ["still"] = new("neutral", 0, (3000, 7000), null, Array.Empty<string>()),
    };
}

////////// 7. REACTIONS //////////

public sealed record Reaction(string? Expression = null, string? Gesture = null, string? Mood = null, string? Then = null);

static class Reactions
{
    public static readonly Dictionary<string, Reaction> All = new()
    {
        // The web greet has no Then because a panel opens after it. On the desktop he would grin all day.
        ["greet"] = new("grin", "hop", Then: "calm"),
        ["sent"] = new("grin", "hop", Then: "calm"),
        ["saved"] = new("happy", "pop", Then: "calm"),
        ["thinking"] = new("thinking", Mood: "working"),
        ["answered"] = new("happy", "nod", Then: "calm"),
        ["doubt"] = new("suspicious", "peek", Then: "calm"),
        ["stuck"] = new("sad", "shake", Then: "alert"),
        ["no"] = new("neutral", "shake"),
        ["yes"] = new("happy", "nod"),
        ["celebrate"] = new("grin", "spin", Then: "happy"),
        ["tease"] = new("cheeky", "wobble"),
        ["dizzy"] = new("dizzy", "dizzy", Then: "calm"),
        ["flip"] = new("surprised", "backflip", Then: "happy"),
        ["tickled"] = new("grin", "giggle", Then: "happy"),
        ["idle"] = new("sleepy", Mood: "sleepy"),
        ["wake"] = new("surprised", "pop", Then: "calm"),
        ["grabbed"] = new("surprised", "pop"),
        ["dropped"] = new("happy", "land", Then: "calm"),
    };
}

////////// 8. VISEMES //////////

// Open, width and curve of the mouth for a sound, and Pause in extra ms after it, which is a sentence's rhythm.
public sealed record Viseme(double Open, double Width, double Curve, double Pause);

static class Visemes
{
    static readonly Viseme Rest = new(0, 0.88, 0.5, 0), MBP = new(0, 0.76, 0.12, 34), FV = new(0.16, 0.72, 0.16, 12),
        TH = new(0.32, 0.8, 0.2, 10), L = new(0.38, 0.86, 0.28, 0), WQ = new(0.34, 0.42, 0.1, 0), OH = new(0.66, 0.54, 0.14, 4),
        EE = new(0.38, 1.16, 0.42, 0), AH = new(0.82, 0.96, 0.22, 6), EH = new(0.52, 1.04, 0.32, 0), SDT = new(0.2, 0.94, 0.26, 0),
        CH = new(0.28, 0.62, 0.1, 8), Space = new(0.05, 0.88, 0.42, 26), Comma = new(0.04, 0.86, 0.45, 140), Stop = new(0, 0.86, 0.5, 280);

    static readonly Dictionary<char, Viseme> Single = new()
    {
        ['a'] = AH, ['e'] = EH, ['i'] = EE, ['o'] = OH, ['u'] = WQ, ['y'] = EE,
        ['m'] = MBP, ['b'] = MBP, ['p'] = MBP, ['f'] = FV, ['v'] = FV,
        ['w'] = WQ, ['q'] = WQ, ['l'] = L, ['r'] = L, ['j'] = CH,
    };

    // Neighbours matter because English spells one sound with two letters: th, sh, ch, oo, ee.
    public static Viseme For(char prev, char c, char next)
    {
        c = char.ToLowerInvariant(c);
        string ahead = $"{c}{char.ToLowerInvariant(next)}", behind = $"{char.ToLowerInvariant(prev)}{c}";
        bool pair(params string[] s) => s.Contains(ahead) || s.Contains(behind);
        if (pair("th")) return TH;
        if (pair("ch", "sh")) return CH;
        if (pair("oo", "ou")) return WQ;
        if (pair("ee", "ea")) return EE;
        if (".!?".Contains(c)) return Stop;
        if (",;:".Contains(c)) return Comma;
        if (char.IsWhiteSpace(c)) return Space;
        if (Single.TryGetValue(c, out var v)) return v;
        return char.IsLetterOrDigit(c) ? SDT : Rest;
    }
}
