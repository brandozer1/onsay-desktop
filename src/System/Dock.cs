// Dock.cs - where on the screen he and his chat sit. Pure arithmetic over a monitor's rectangles, in pixels.
// He hovers on the taskbar edge, wherever it is. Along is 0 at the start of the bar and 1 at the tray end.
//
// 1. EdgeOf(m)                              which edge the taskbar is on
// 2. Place(m, along, w, h, margin, inset)   his window for a point along the bar
// 3. AlongOf(m, topLeft, w, h, margin)      the other way: how far along the bar a window is
// 4. Beside(m, body, w, h, gap, prefer)     a panel next to him, off the bar side if it fits there, kept on screen
// 5. OnBar(m, body, near)                   whether his body was dropped on the bar, or within near of it
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
        RECT mon = m.rcMonitor, work = m.rcWork;
        return work.Bottom < mon.Bottom ? Edge.Bottom
            : work.Top > mon.Top ? Edge.Top
            : work.Left > mon.Left ? Edge.Left
            : work.Right < mon.Right ? Edge.Right
            : Edge.Bottom;
    }

    // 2. inset pushes the window over the bar, so his transparent padding overlaps the bar and not your work.
    public static POINT Place(MONITORINFO m, double along, int w, int h, int margin, int inset)
    {
        var work = m.rcWork;
        var edge = EdgeOf(m);
        along = Math.Clamp(along, 0, 1);
        if (Horizontal(edge))
            return new POINT(work.Left + margin + (int)Math.Round(along * Math.Max(0, work.Width - w - 2 * margin)),
                edge == Edge.Bottom ? work.Bottom - h + inset : work.Top - inset);
        return new POINT(edge == Edge.Right ? work.Right - w + inset : work.Left - inset,
            work.Top + margin + (int)Math.Round(along * Math.Max(0, work.Height - h - 2 * margin)));
    }

    // 3.
    public static double AlongOf(MONITORINFO m, POINT topLeft, int w, int h, int margin)
    {
        var work = m.rcWork;
        bool flat = Horizontal(EdgeOf(m));
        int span = flat ? work.Width - w - 2 * margin : work.Height - h - 2 * margin;
        int at = flat ? topLeft.X - work.Left - margin : topLeft.Y - work.Top - margin;
        return span <= 0 ? 0.5 : Math.Clamp((double)at / span, 0, 1);
    }

    /* 4. Away from the bar first, the way he faces when docked. Put somewhere else on the screen there may be no room on
          that side, so it tries the other sides in turn and takes the first it fits on whole, never covering him. Only
          where it fits on none, on a small screen, it takes the one with most room and may overlap him. side is the one it
          went to. prefer keeps it on that side while it still fits there, so moving him along never flips the panel to the
          other side of him and back. */
    public static POINT Beside(MONITORINFO m, RECT body, int w, int h, int gap, Side? prefer, out Side side)
    {
        var work = m.rcWork;
        int cx = (body.Left + body.Right - w) / 2, cy = (body.Top + body.Bottom - h) / 2;
        var above = (side: Side.Above, p: new POINT(cx, body.Top - gap - h), room: body.Top - gap - work.Top - h);
        var below = (side: Side.Below, p: new POINT(cx, body.Bottom + gap), room: work.Bottom - body.Bottom - gap - h);
        var right = (side: Side.Right, p: new POINT(body.Right + gap, cy), room: work.Right - body.Right - gap - w);
        var left = (side: Side.Left, p: new POINT(body.Left - gap - w, cy), room: body.Left - gap - work.Left - w);
        var order = EdgeOf(m) switch
        {
            Edge.Bottom => new[] { above, below, right, left },
            Edge.Top => new[] { below, above, right, left },
            Edge.Left => new[] { right, left, above, below },
            _ => new[] { left, right, above, below },
        };
        var kept = order.FirstOrDefault(s => s.side == prefer && s.room >= 0);
        var pick = kept.side == prefer && prefer is not null ? kept : order.FirstOrDefault(s => s.room >= 0, order.MaxBy(s => s.room));
        side = pick.side;
        return new POINT(Math.Clamp(pick.p.X, work.Left, Math.Max(work.Left, work.Right - w)),
            Math.Clamp(pick.p.Y, work.Top, Math.Max(work.Top, work.Bottom - h)));
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
}
