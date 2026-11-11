// Dock.cs - where on the screen he and his chat sit. Pure arithmetic over a monitor's rectangles, in pixels.
// He hovers on the taskbar edge, wherever it is. Along is 0 at the start of the bar and 1 at the tray end.
//
// 1. EdgeOf(m)                              which edge the taskbar is on
// 2. Place(m, along, w, h, margin, inset)   his window for a point along the bar
// 3. AlongOf(m, topLeft, w, h, margin)      the other way: how far along the bar a window is
// 4. Beside(m, body, w, h, gap, prefer)     a card next to him, toward the middle of the screen, sliding along him
// 5. OnBar(m, body, near)                   whether his body was dropped on the bar, or within near of it
// 6. Clear(work, body, card, gap)           how far his body moves, the shortest way, to be outside a card and gap round it
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

enum Edge { Bottom, Top, Left, Right }
enum Side { Above, Below, Left, Right }   // where a panel sits beside him

static class Dock
{
    static bool Horizontal(Edge e) => e is Edge.Bottom or Edge.Top;

    // 1. The side the work area gives up. Nothing given up is an auto hiding taskbar, so the bottom.
    public static Edge EdgeOf(MONITORINFO m)
    {
        var mon = m.rcMonitor;
        var work = m.rcWork;
        if (work.Bottom < mon.Bottom) return Edge.Bottom;
        if (work.Top > mon.Top) return Edge.Top;
        if (work.Left > mon.Left) return Edge.Left;
        if (work.Right < mon.Right) return Edge.Right;
        return Edge.Bottom;
    }

    // 2. inset pushes the window over the bar, so his transparent padding overlaps the bar and not your work.
    public static POINT Place(MONITORINFO m, double along, int w, int h, int margin, int inset)
    {
        var work = m.rcWork;
        var edge = EdgeOf(m);
        along = Compat.Clamp(along, 0, 1);
        if (Horizontal(edge))
        {
            int x = work.Left + margin + (int)Math.Round(along * Math.Max(0, work.Width - w - 2 * margin));
            int y = edge == Edge.Bottom ? work.Bottom - h + inset : work.Top - inset;
            return new POINT(x, y);
        }
        int sideX = edge == Edge.Right ? work.Right - w + inset : work.Left - inset;
        int sideY = work.Top + margin + (int)Math.Round(along * Math.Max(0, work.Height - h - 2 * margin));
        return new POINT(sideX, sideY);
    }

    // 3.
    public static double AlongOf(MONITORINFO m, POINT topLeft, int w, int h, int margin)
    {
        var work = m.rcWork;
        bool flat = Horizontal(EdgeOf(m));
        int span = flat ? work.Width - w - 2 * margin : work.Height - h - 2 * margin;
        int at = flat ? topLeft.X - work.Left - margin : topLeft.Y - work.Top - margin;
        return span <= 0 ? 0.5 : Compat.Clamp((double)at / span, 0, 1);
    }

    /* 4. Toward the middle of the screen, the way a tooltip opens: above him on the lower half, below him on the upper
          half, or, where neither fits whole, beside him toward the middle. Along him it slides smoothly with where he is:
          at the left of the screen it reaches right from him, at the right it reaches left, in the middle it is centred,
          so it is always on screen without being pushed and never jumps as he moves. side is the one it went to. prefer
          keeps it on that side while it still fits there, so dragging him never flips it back and forth. avoid is a side
          taken already, like the chat's, left alone while another fits. Only where it fits on no side, on a small
          screen, it takes the one with most room and may overlap him. */
    public static POINT Beside(MONITORINFO m, RECT body, int w, int h, int gap, Side? prefer, out Side side, Side? avoid = null)
    {
        var work = m.rcWork;
        var him = body.Center;
        double across = Compat.Clamp((double)(him.X - work.Left) / Math.Max(1, work.Width), 0, 1);
        double down = Compat.Clamp((double)(him.Y - work.Top) / Math.Max(1, work.Height), 0, 1);
        int x = him.X - (int)Math.Round(w * across);
        int y = him.Y - (int)Math.Round(h * down);
        var above = (side: Side.Above, p: new POINT(x, body.Top - gap - h), room: body.Top - gap - work.Top - h);
        var below = (side: Side.Below, p: new POINT(x, body.Bottom + gap), room: work.Bottom - body.Bottom - gap - h);
        var right = (side: Side.Right, p: new POINT(body.Right + gap, y), room: work.Right - body.Right - gap - w);
        var left = (side: Side.Left, p: new POINT(body.Left - gap - w, y), room: body.Left - gap - work.Left - w);
        var vertical = down > 0.5 ? new[] { above, below } : new[] { below, above };
        var sideways = across > 0.5 ? new[] { left, right } : new[] { right, left };
        var order = vertical.Concat(sideways).ToArray();
        if (avoid is not null && order.Any(s => s.side != avoid && s.room >= 0)) order = order.Where(s => s.side != avoid).ToArray();
        var kept = order.FirstOrDefault(s => s.side == prefer && s.room >= 0);
        var pick = kept.side == prefer && prefer is not null ? kept : order.Where(s => s.room >= 0).DefaultIfEmpty(order.MaxBy(s => s.room)).First();
        side = pick.side;
        int keptX = Compat.Clamp(pick.p.X, work.Left, Math.Max(work.Left, work.Right - w));
        int keptY = Compat.Clamp(pick.p.Y, work.Top, Math.Max(work.Top, work.Bottom - h));
        return new POINT(keptX, keptY);
    }

    // 5.
    public static bool OnBar(MONITORINFO m, RECT body, int near)
    {
        var work = m.rcWork;
        return EdgeOf(m) switch
        {
            Edge.Bottom => body.Bottom >= work.Bottom - near,
            Edge.Top => body.Top <= work.Top + near,
            Edge.Left => body.Left <= work.Left + near,
            _ => body.Right >= work.Right - near,
        };
    }

    // 6. Nothing when he is clear of it already. Each way out keeps him on the screen along the way he moves.
    public static POINT Clear(RECT work, RECT body, RECT card, int gap)
    {
        var room = new RECT { Left = card.Left - gap, Top = card.Top - gap, Right = card.Right + gap, Bottom = card.Bottom + gap };
        bool over = body.Left < room.Right && body.Right > room.Left && body.Top < room.Bottom && body.Bottom > room.Top;
        if (!over) return new POINT(0, 0);
        var ways = new[]
        {
            new POINT(room.Left - body.Right, 0),
            new POINT(room.Right - body.Left, 0),
            new POINT(0, room.Top - body.Bottom),
            new POINT(0, room.Bottom - body.Top),
        };
        var fits = ways.Where(w => w.X != 0
            ? body.Left + w.X >= work.Left && body.Right + w.X <= work.Right
            : body.Top + w.Y >= work.Top && body.Bottom + w.Y <= work.Bottom);
        return fits.DefaultIfEmpty(ways[0]).MinBy(w => Math.Abs(w.X) + Math.Abs(w.Y));
    }
}
