using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVATacScan.Engine;

namespace AVATacScan.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVATacScan/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        width = Math.Round(width * 4) / 4;
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFEAF4FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF7F8CA8, Cyan = 0xFF4AF0FF,
        Green = 0xFF6AFF7A, Magenta = 0xFFFF4AD8, Orange = 0xFFFF8A3A, Blue = 0xFF3A6CFF, Violet = 0xFF9A6AFF;

    private const double W = Field.Width, H = Field.Height;

    /// <summary>Text with a soft glow behind it, like the beam on a vector screen.</summary>
    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool glow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        var pos = new Point(x, at.Y - ft.Height / 2);
        if (glow)
        {
            var halo = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour & 0x30FFFFFF));
            foreach (var (ox, oy) in new[] { (-1.5, 0.0), (1.5, 0.0), (0.0, -1.5), (0.0, 1.5) }) dc.DrawText(halo, new Point(pos.X + ox, pos.Y + oy));
        }
        dc.DrawText(ft, pos);
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>A line as the beam draws it: a wide faint halo, a narrower one, and the bright core.</summary>
    private static void Beam(DrawingContext dc, Point a, Point b, uint colour, double bright = 1, double core = 1.5)
    {
        if (bright <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.12 * bright), core * 5), a, b);
        dc.DrawLine(Pen(Alpha(colour, 0.3 * bright), core * 2.5), a, b);
        dc.DrawLine(Pen(Alpha(colour, bright), core), a, b);
    }

    /// <summary>A closed outline of beams.</summary>
    private static void Outline(DrawingContext dc, IReadOnlyList<Point> pts, uint colour, double bright = 1, double core = 1.5)
    {
        for (int i = 0; i < pts.Count; i++) Beam(dc, pts[i], pts[(i + 1) % pts.Count], colour, bright, core);
    }

    /// <summary>A shape drawn about a point: <paramref name="shape"/>'s points are across and forward (forward is up the
    /// page before turning), scaled, turned to face <paramref name="fx"/>,<paramref name="fy"/>, and squashed along
    /// the way it faces by <paramref name="depth"/> (a ship seen from behind, flying into the screen).</summary>
    private static Point[] Place((double X, double Y)[] shape, double cx, double cy, double fx, double fy, double scale, double depth = 1)
    {
        double l = Math.Max(1e-9, double.Hypot(fx, fy));
        fx /= l;
        fy /= l;
        // Across is the facing turned a quarter right.
        double ax = -fy, ay = fx;
        var pts = new Point[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            var (sx, sf) = shape[i];
            pts[i] = new Point(cx + (ax * sx + fx * sf * depth) * scale, cy + (ay * sx + fy * sf * depth) * scale);
        }
        return pts;
    }

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02030A), new Rect(0, 0, W, H));
        if (InGame) DrawPlay(dc, g.World);
        else DrawBackdrop(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default:
                DrawHud(dc);
                DrawMessages(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(W / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(W / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(W / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(W / 2, H - 16), 24, Yellow, center: true);
        if (_toast != null && _toastTime > 0)
        {
            dc.FillRectangle(Brush(0xB0000000), new Rect(W / 2 - 300, H - 92, 600, 40), 8);
            Text(dc, _toast, new Point(W / 2, H - 72), 26, Cyan, center: true);
        }
    }

    // ───────────────────────────── the stars ─────────────────────────────

    private readonly (double X, double Y, double Z, uint Colour)[] _stars = new (double, double, double, uint)[140];
    private readonly Random _starRng = new(7);

    private void InitStars()
    {
        uint[] tints = [White, 0xFFB0C8FF, 0xFFFFE0B0, 0xFF9AFFF0];
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = (_starRng.NextDouble() * W, _starRng.NextDouble() * H, 1 + _starRng.NextDouble() * 10, tints[i % tints.Length]);
    }

    /// <summary>How much the stars stream out of the vanishing point (0 on the flat, 1 in the tunnel).</summary>
    private double Streak => InGame ? Engine.WarpFraction : 1;

    private void MoveStars(double dt)
    {
        double streak = Streak, warp = InGame && Engine.Phase == Phase.Warp ? 4 : 1;
        for (int i = 0; i < _stars.Length; i++)
        {
            var (x, y, z, c) = _stars[i];
            // On the flat they drift down the screen, nearer ones faster; in the tunnel they fly out of the middle.
            y += (1 - streak) * 60 / z * dt;
            z -= streak * 2.2 * warp * dt;
            if (y > H) { y -= H; x = _starRng.NextDouble() * W; }
            if (z < 0.3)
            {
                z = 8 + _starRng.NextDouble() * 4;
                (x, y) = (_starRng.NextDouble() * W, _starRng.NextDouble() * H);
            }
            _stars[i] = (x, y, z, c);
        }
    }

    private void DrawStars(DrawingContext dc)
    {
        double streak = Streak;
        foreach (var (x, y, z, c) in _stars)
        {
            double bright = Math.Clamp(1.4 / z, 0.15, 1);
            if (streak < 0.02)
            {
                dc.DrawRectangle(Brush(Alpha(c, bright)), null, new Rect(x, y, 2, 2));
                continue;
            }
            // Out from the vanishing point: the star's place on the screen is its direction, pushed out by nearness.
            if (z < 0.9) continue;
            double dx = (x - Field.Cx) / W * 6, dy = (y - Field.Cy) / H * 6;
            var a = new Point(Field.Cx + dx / z * 140, Field.Cy + dy / z * 140);
            double tail = InGame && Engine.Phase == Phase.Warp ? 0.3 + streak * 2.5 : 0.15;
            var b = new Point(Field.Cx + dx / (z + tail) * 140, Field.Cy + dy / (z + tail) * 140);
            bright *= InGame && Engine.Phase == Phase.Warp ? 1 : 0.6;
            dc.DrawLine(Pen(Alpha(c, bright), 1.5), a, b);
        }
    }

    // ───────────────────────────── the play ─────────────────────────────

    private void DrawPlay(DrawingContext dc, World w)
    {
        DrawStars(dc);
        double f = Engine.WarpFraction;
        if (f > 0) DrawTube(dc, w.Scroll, f);
        if (f < 1) DrawTrack(dc, 1 - f);
        if (!w.Tunnel)
        {
            foreach (var e in w.Enemies) DrawEnemy(dc, e);
            foreach (var b in w.Bullets) DrawBullet(dc, b);
            foreach (var s in w.Shots) Beam(dc, new Point(s.X, s.Y), new Point(s.X - s.Vx * 0.018, s.Y - s.Vy * 0.018), 0xFFFFF0A0, 1, 2);
        }
        else
        {
            if (w.Mother is { } m) DrawMother(dc, w, m);
            // Furthest first.
            foreach (var e in w.Incoming.OrderByDescending(e => e.Z)) DrawIncoming(dc, e);
            foreach (var b in w.Bolts)
            {
                var (x, y, s) = Field.Tube(b.Pos, b.Z);
                double r = Math.Max(2, 9 * s);
                Outline(dc, [new(x, y - r), new(x + r, y), new(x, y + r), new(x - r, y)], Orange, 1, 1.4);
            }
            foreach (var s in w.Shots)
            {
                var (x0, y0, _) = Field.Tube(s.Pos, s.Z);
                var (x1, y1, _) = Field.Tube(s.Pos, s.Z + 0.35);
                Beam(dc, new Point(x0, y0), new Point(x1, y1), 0xFFFFF0A0, 1, 2);
            }
        }
        DrawSquadron(dc, w, f);
        foreach (var b in w.Blasts) DrawBlast(dc, b);
    }

    /// <summary>The flat's track: a faint arc of dashes at the foot of the screen, the way the squadron can go.</summary>
    private static void DrawTrack(DrawingContext dc, double bright)
    {
        for (int i = 0; i < 40; i++)
        {
            double p0 = -1.1 + i * 0.055, p1 = p0 + 0.025;
            var (x0, y0, _, _) = Field.Flat(p0, 62);
            var (x1, y1, _, _) = Field.Flat(p1, 62);
            Beam(dc, new Point(x0, y0), new Point(x1, y1), Blue, 0.45 * bright, 1);
        }
    }

    /// <summary>The tunnel: rings of the tube rushing out of the vanishing point, and the lines along its walls.</summary>
    private static void DrawTube(DrawingContext dc, double scroll, double bright)
    {
        const int spokes = 16, segments = 48;
        double frac = scroll % 1;
        for (int k = 0; k < (int)Field.FarZ; k++)
        {
            double z = k + 1 - frac;
            if (z < 0.5) continue;
            double fade = Math.Clamp(1.6 / z, 0.12, 0.8) * bright * Math.Clamp((Field.FarZ - z) / 2, 0, 1);
            var pts = new Point[segments];
            for (int i = 0; i < segments; i++)
            {
                var (x, y) = Field.TubeAt(i * Math.PI * 2 / segments, z);
                pts[i] = new Point(x, y);
            }
            Outline(dc, pts, Violet, fade, 1.1);
        }
        for (int i = 0; i < spokes; i++)
        {
            double a = i * Math.PI * 2 / spokes;
            var (x0, y0) = Field.TubeAt(a, 0.75);
            var (x1, y1) = Field.TubeAt(a, Field.FarZ);
            Beam(dc, new Point(x0, y0), new Point(x1, y1), Blue, 0.5 * bright, 1);
        }
        // The track round the near rim, where the squadron flies.
        for (int i = 0; i < 30; i++)
        {
            double p0 = -1 + i * (2.0 / 30), p1 = p0 + 2.0 / 30 * 0.55;
            var (x0, y0, _) = Field.Tube(p0, Field.ShipZ - 0.2);
            var (x1, y1, _) = Field.Tube(p1, Field.ShipZ - 0.2);
            Beam(dc, new Point(x0, y0), new Point(x1, y1), Cyan, 0.35 * bright, 1);
        }
    }

    // ───────────────────────────── the squadron ─────────────────────────────

    /// <summary>A Tac/Scan-style fighter seen from above: a sharp nose, swept wings and a notched tail.</summary>
    private static readonly (double X, double Y)[] ShipShape =
        [(0, 15), (4, 4), (13, -6), (13, -9), (5, -5), (3, -9), (-3, -9), (-5, -5), (-13, -9), (-13, -6), (-4, 4)];

    private void DrawSquadron(DrawingContext dc, World w, double warp)
    {
        double bank = Math.Clamp(w.LeadSpeed / Field.SpinSpeed, -1, 1);
        for (int i = w.Ships.Count - 1; i >= 0; i--)
        {
            var s = w.Ships[i];
            double pos = Sim.PosOf(w, s);
            var (fx, fy, fdx, fdy) = Field.Flat(pos, s.Row * Field.FlatRow);
            var (tx, ty, scale) = Field.Tube(pos, Sim.DepthOf(s));
            // On the warp the squadron sweeps from the flat's track round onto the tunnel's rim.
            double x = fx + (tx - fx) * warp, y = fy + (ty - fy) * warp;
            double tl = Math.Max(1, double.Hypot(Field.Cx - tx, Field.Cy - ty));
            double dx = fdx + ((Field.Cx - tx) / tl - fdx) * warp, dy = fdy + ((Field.Cy - ty) / tl - fdy) * warp;
            double size = (1 + (scale * 1.5 - 1) * warp) * 1.1;
            // Seen from behind as it flies into the tube, the ship is foreshortened; banking narrows it as it turns.
            double depth = 1 - 0.55 * warp;
            var shape = ShipShape.Select(p => (p.X * (1 - Math.Abs(bank) * 0.18), p.Y)).ToArray();
            var pts = Place(shape, x, y, dx, dy, size, depth);
            uint colour = i == 0 ? White : Cyan;
            double flash = s.Age < 1 && (int)(s.Age * 10) % 2 == 0 ? 0.35 : 1;
            Outline(dc, pts, colour, flash, 1.6);
            // The engine's glow at the tail.
            var tail = Place([(0, -9)], x, y, dx, dy, size, depth)[0];
            dc.DrawEllipse(Brush(Alpha(Orange, 0.55 + 0.3 * Math.Sin(_clock * 30 + i))), null, tail, 3 * size, 3 * size);
        }
    }

    // ───────────────────────────── the flat's enemies ─────────────────────────────

    private static readonly (double X, double Y)[] DartShape = [(0, 14), (8, -8), (0, -3), (-8, -8)];

    private void DrawEnemy(DrawingContext dc, Enemy e)
    {
        uint c = e.Flash > 0 ? 0xFFFFFFFF : Sim.Colour(e.Kind);
        double x = e.X, y = e.Y;
        switch (e.Kind)
        {
            case Foe.Saucer:
            {
                var rim = Enumerable.Range(0, 12).Select(i => new Point(x + Math.Cos(i * Math.PI / 6) * 19, y + Math.Sin(i * Math.PI / 6) * 6)).ToArray();
                Outline(dc, rim, c);
                var dome = Enumerable.Range(0, 7).Select(i => new Point(x + Math.Cos(Math.PI + i * Math.PI / 6) * 9, y - 2 + Math.Sin(Math.PI + i * Math.PI / 6) * 9)).ToArray();
                for (int i = 0; i < dome.Length - 1; i++) Beam(dc, dome[i], dome[i + 1], c);
                for (int i = 0; i < 3; i++)
                {
                    double lx = x - 10 + i * 10 + Math.Sin(_clock * 6) * 2;
                    dc.DrawEllipse(Brush(Alpha(Yellow, (int)(_clock * 8 + i) % 3 == 0 ? 1 : 0.4)), null, new Point(lx, y + 1), 2, 2);
                }
                break;
            }
            case Foe.Dart:
            {
                double vx = e.Vx, vy = e.Vy;
                if (Math.Abs(vx) + Math.Abs(vy) < 1) vy = 1;
                Outline(dc, Place(DartShape, x, y, vx, vy, 1.1), c);
                break;
            }
            case Foe.Spinner:
            {
                for (int k = 0; k < 2; k++)
                {
                    double a = e.Spin + k * Math.PI / 4, r = k == 0 ? 20 : 13;
                    var pts = Enumerable.Range(0, 4).Select(i => new Point(x + Math.Cos(a + i * Math.PI / 2) * r, y + Math.Sin(a + i * Math.PI / 2) * r)).ToArray();
                    Outline(dc, pts, k == 0 ? c : White, 1, 1.4);
                }
                if (e.Hp > 1) dc.DrawEllipse(Brush(c), null, new Point(x, y), 3, 3);
                break;
            }
            case Foe.Bomber:
            {
                double d = e.Vx >= 0 ? 1 : -1;
                Point P(double px, double py) => new(x + px * d, y + py);
                Outline(dc, [P(26, 0), P(12, -8), P(-18, -8), P(-26, -2), P(-26, 4), P(-18, 8), P(12, 8)], c);
                Outline(dc, [P(4, -8), P(-8, -20), P(-14, -20), P(-10, -8)], c, 0.8, 1.2);
                Outline(dc, [P(4, 8), P(-8, 20), P(-14, 20), P(-10, 8)], c, 0.8, 1.2);
                Beam(dc, P(14, -3), P(20, 0), White, 0.8, 1.2);
                break;
            }
        }
    }

    private static void DrawBullet(DrawingContext dc, Bullet b)
    {
        if (b.Bomb)
        {
            Outline(dc, Enumerable.Range(0, 6).Select(i => new Point(b.X + Math.Cos(i * Math.PI / 3) * 6, b.Y + Math.Sin(i * Math.PI / 3) * 6)).ToArray(), Orange, 1, 1.4);
            return;
        }
        Beam(dc, new Point(b.X - 4, b.Y), new Point(b.X + 4, b.Y), Red, 1, 1.6);
        Beam(dc, new Point(b.X, b.Y - 4), new Point(b.X, b.Y + 4), Red, 1, 1.6);
    }

    // ───────────────────────────── the tunnel's threats ─────────────────────────────

    private void DrawIncoming(DrawingContext dc, Incoming e)
    {
        double near = Math.Clamp((Field.FarZ - e.Z) / 2, 0, 1);
        if (e.Kind == Threat.Barrier)
        {
            // A band of force: two arcs, a zig-zag between them.
            uint c = e.Flash > 0 ? White : Red;
            int n = Math.Max(3, (int)((e.To - e.Pos) * 24));
            var front = new List<Point>();
            var back = new List<Point>();
            for (int i = 0; i <= n; i++)
            {
                double p = e.Pos + (e.To - e.Pos) * i / n;
                var (x0, y0, _) = Field.Tube(p, e.Z, 1.0);
                var (x1, y1, _) = Field.Tube(p, e.Z, 0.78);
                front.Add(new Point(x0, y0));
                back.Add(new Point(x1, y1));
            }
            for (int i = 0; i < n; i++)
            {
                Beam(dc, front[i], front[i + 1], c, near, 1.8);
                Beam(dc, back[i], back[i + 1], c, near, 1.4);
                Beam(dc, (i + (int)(_clock * 12)) % 2 == 0 ? front[i] : back[i], (i + (int)(_clock * 12)) % 2 == 0 ? back[i + 1] : front[i + 1], Orange, near * 0.8, 1.1);
            }
            Beam(dc, front[0], back[0], c, near, 1.6);
            Beam(dc, front[n], back[n], c, near, 1.6);
            return;
        }
        var (x, y, s) = Field.Tube(e.Pos, e.Z);
        double r = 26 * s;
        if (e.Kind == Threat.Fighter)
        {
            uint c = e.Flash > 0 ? White : Green;
            // Head on: a diamond body with swept wings either side.
            Outline(dc, [new(x, y - r * 0.6), new(x + r * 0.45, y), new(x, y + r * 0.6), new(x - r * 0.45, y)], c, near);
            Beam(dc, new Point(x + r * 0.45, y), new Point(x + r * 1.3, y - r * 0.5), c, near);
            Beam(dc, new Point(x + r * 1.3, y - r * 0.5), new Point(x + r * 1.3, y + r * 0.4), c, near);
            Beam(dc, new Point(x - r * 0.45, y), new Point(x - r * 1.3, y - r * 0.5), c, near);
            Beam(dc, new Point(x - r * 1.3, y - r * 0.5), new Point(x - r * 1.3, y + r * 0.4), c, near);
            dc.DrawEllipse(Brush(Alpha(Yellow, near)), null, new Point(x, y), Math.Max(1, r * 0.12), Math.Max(1, r * 0.12));
        }
        else
        {
            uint c = e.Flash > 0 ? White : Magenta;
            double a = e.T * 4;
            var tri = Enumerable.Range(0, 3).Select(i => new Point(x + Math.Cos(a + i * Math.PI * 2 / 3) * r, y + Math.Sin(a + i * Math.PI * 2 / 3) * r)).ToArray();
            var tri2 = Enumerable.Range(0, 3).Select(i => new Point(x + Math.Cos(-a + i * Math.PI * 2 / 3) * r * 0.6, y + Math.Sin(-a + i * Math.PI * 2 / 3) * r * 0.6)).ToArray();
            Outline(dc, tri, c, near);
            Outline(dc, tri2, White, near * 0.8, 1.1);
        }
    }

    /// <summary>The mother ship: a great wheel filling the tube, its docking bay lit on the rim, a beam down to the track to line up on.</summary>
    private void DrawMother(DrawingContext dc, World w, MotherShip m)
    {
        double z = m.Z;
        if (z > Field.FarZ + 1.5) return;
        double near = Math.Clamp((Field.FarZ + 1.5 - z) / 2, 0, 1);
        Point Ring(double angle, double r)
        {
            var (x, y) = Field.TubeAt(angle, z, r);
            return new Point(x, y);
        }
        // An eight-sided hull turning slowly, dark inside so the tube behind it is hidden.
        double turn = m.T * 0.25;
        var hull = Enumerable.Range(0, 8).Select(i => Ring(turn + i * Math.PI / 4, 0.94)).ToArray();
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(hull[0], true);
            foreach (var p in hull.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(Alpha(0xFF050812, 0.92 * near)), null, g);
        Outline(dc, hull, Yellow, near, 1.8);
        var inner = Enumerable.Range(0, 24).Select(i => Ring(-turn * 2 + i * Math.PI / 12, 0.55)).ToArray();
        Outline(dc, inner, Orange, near * 0.9, 1.5);
        for (int i = 0; i < 8; i++)
        {
            // The struts out to the hull's corners, a pod on each.
            double a = turn + i * Math.PI / 4;
            Beam(dc, Ring(a, 0.3), Ring(a, 0.94), Orange, near * 0.7, 1.2);
            Outline(dc, [Ring(a - 0.06, 0.8), Ring(a + 0.06, 0.8), Ring(a + 0.05, 0.9), Ring(a - 0.05, 0.9)], Cyan, near * 0.8, 1.1);
            if ((i + (int)(m.T * 5)) % 4 == 0) dc.DrawEllipse(Brush(Alpha(Red, near)), null, Ring(a + Math.PI / 8, 0.75), 3.5, 3.5);
        }
        var hub = Enumerable.Range(0, 6).Select(i => Ring(turn * 3 + i * Math.PI / 3, 0.28)).ToArray();
        Outline(dc, hub, White, near, 1.6);
        var (cx, cy) = Field.TubeAt(0, z, 0);
        double glow = (0.12 + 0.05 * Math.Sin(m.T * 6)) * Field.Focal / z;
        dc.DrawEllipse(Brush(Alpha(Yellow, 0.35 * near)), null, new Point(cx, cy), glow, glow);
        // The bay: a lit notch in the rim, and its guide beam down the tube to the track.
        bool lined = Math.Abs(w.Lead - m.Bay) < Field.DockReach;
        uint bay = lined ? Green : (int)(m.T * 4) % 2 == 0 ? Red : Yellow;
        var (b0x, b0y, _) = Field.Tube(m.Bay - 0.08, z, 0.76);
        var (b1x, b1y, _) = Field.Tube(m.Bay + 0.08, z, 0.76);
        var (b2x, b2y, _) = Field.Tube(m.Bay + 0.08, z, 1.0);
        var (b3x, b3y, _) = Field.Tube(m.Bay - 0.08, z, 1.0);
        var (b4x, b4y, _) = Field.Tube(m.Bay, z, 0.82);
        dc.DrawEllipse(Brush(Alpha(bay, 0.5 * near)), null, new Point(b4x, b4y), 6 * Field.Focal / z / 100, 6 * Field.Focal / z / 100);
        Outline(dc, [new(b0x, b0y), new(b1x, b1y), new(b2x, b2y), new(b3x, b3y)], bay, near, 2.2);
        if (!m.Arrived)
        {
            var (gx, gy, _) = Field.Tube(m.Bay, z, 1.0);
            var (hx, hy, _) = Field.Tube(m.Bay, Field.ShipZ + 0.3, 1.0);
            Beam(dc, new Point(gx, gy), new Point(hx, hy), bay, near * 0.5, 1.2);
            // The mark on the track, just ahead of where the leader must be, pointing at it.
            var (mx, my, _) = Field.Tube(m.Bay, Field.ShipZ + 0.3, 1.0);
            double ux = mx - Field.Cx, uy = my - Field.Cy, ul = Math.Max(1, double.Hypot(ux, uy));
            (ux, uy) = (ux / ul, uy / ul);
            Outline(dc, [new(mx - ux * 14 - uy * 12, my - uy * 14 + ux * 12), new(mx - ux * 14 + uy * 12, my - uy * 14 - ux * 12), new(mx, my)], bay, near, 1.8);
        }
    }

    private static void DrawBlast(DrawingContext dc, Blast b)
    {
        double f = b.T / 0.8, r = b.Size * (0.3 + f);
        double fade = 1 - f;
        foreach (double a in b.Angles)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            Beam(dc, new Point(b.X + c * r * 0.4, b.Y + s * r * 0.4), new Point(b.X + c * r, b.Y + s * r), b.Colour, fade, 1.4);
        }
        if (f < 0.3) dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, (0.3 - f) * 2)), null, new Point(b.X, b.Y), b.Size * 0.3, b.Size * 0.3);
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private static string Points(int n) => n.ToString("N0").Replace(",", " ");

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        Text(dc, "SCORE", new Point(24, 20), 22, Magenta);
        Text(dc, Points(g.Score), new Point(24, 48), 36, White);
        Text(dc, "HIGH", new Point(W / 2, 20), 22, Yellow, center: true);
        Text(dc, Points(Math.Max(g.HighScores.TopScore, g.Score)), new Point(W / 2, 48), 36, White, center: true);
        Text(dc, "ROUND " + (w.Round + 1), new Point(W - 24, 20), 26, Cyan, right: true);
        string what = g.Phase switch
        {
            Phase.Warp => "WARP",
            Phase.Tunnel => "TUNNEL",
            Phase.Dock or Phase.Docked => "DOCKING",
            _ when w.Tunnel => "TUNNEL",
            _ => $"WAVE {Math.Min(w.Wave, Field.WavesPerRound - 1) + 1}  LEFT {Math.Max(0, w.Quota - w.Killed)}",
        };
        Text(dc, what, new Point(W - 24, 48), 24, White, right: true);
        if (g.Phase == Phase.Tunnel)
        {
            // How far down the tunnel: the mother ship waits at the end.
            double f = Math.Clamp(w.Flown / Field.TunnelSeconds(w.Round), 0, 1), x = W - 224;
            dc.DrawRectangle(null, Pen(Alpha(Blue, 0.8), 1.5), new Rect(x, 70, 200, 8));
            dc.FillRectangle(Brush(Cyan), new Rect(x + 1, 71, 198 * f, 6));
        }
        // The squadron's strength: a little ship for each one flying.
        Text(dc, "SQUADRON", new Point(24, 80), 20, Dim);
        for (int i = 0; i < w.Ships.Count; i++)
            Outline(dc, Place(ShipShape, 34 + i * 26, 106, 0, -1, 0.7), i == 0 ? White : Cyan, 0.9, 1.1);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = W / 2, cy = 230;
        double size = msg.Length > 16 ? 64 : 84;
        uint c = (int)(_clock * 8) % 2 == 0 ? Yellow : 0xFFFFF4A0;
        Text(dc, msg, new Point(cx, cy), size, c, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + size * 0.6 + 12), 30, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    /// <summary>The tunnel turning slowly behind the title and the tables, a squadron flying round its rim.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        DrawStars(dc);
        DrawTube(dc, _clock * 1.2, 0.55);
        double lead = Math.Sin(_clock * 0.6) * 0.6;
        for (int i = Field.MaxShips - 1; i >= 0; i--)
        {
            var (side, row) = Field.Slots[i];
            double pos = lead + side * Field.SlotSpacing, z = Field.ShipZ - row * Field.TunnelRow;
            var (x, y, s) = Field.Tube(pos, z);
            Outline(dc, Place(ShipShape, x, y, Field.Cx - x, Field.Cy - y, s * 1.3, 0.45), i == 0 ? White : Cyan, 0.8, 1.5);
        }
    }

    /// <summary>The name drawn in strokes, the way the vector monitor's own lettering was.</summary>
    private static readonly Dictionary<char, (double, double, double, double)[]> Strokes = new()
    {
        ['T'] = [(0, 0, 4, 0), (2, 0, 2, 6)],
        ['A'] = [(0, 6, 0, 2), (0, 2, 2, 0), (2, 0, 4, 2), (4, 2, 4, 6), (0, 3.5, 4, 3.5)],
        ['C'] = [(4, 0, 0, 0), (0, 0, 0, 6), (0, 6, 4, 6)],
        ['/'] = [(0, 6, 4, 0)],
        ['S'] = [(4, 0, 0, 0), (0, 0, 0, 3), (0, 3, 4, 3), (4, 3, 4, 6), (4, 6, 0, 6)],
        ['N'] = [(0, 6, 0, 0), (0, 0, 4, 6), (4, 6, 4, 0)],
    };

    private void Logo(DrawingContext dc, double cy, double size)
    {
        const string title = "TAC/SCAN";
        double unit = size / 6, step = unit * 6, x0 = W / 2 - (title.Length * step - unit * 2) / 2;
        uint[] tints = [Cyan, Cyan, Cyan, White, Yellow, Yellow, Yellow, Yellow];
        for (int i = 0; i < title.Length; i++)
        {
            double lx = x0 + i * step, shimmer = 0.85 + 0.15 * Math.Sin(_clock * 3 + i * 0.7);
            foreach (var (ax, ay, bx, by) in Strokes[title[i]])
                Beam(dc, new Point(lx + ax * unit, cy - size / 2 + ay * unit), new Point(lx + bx * unit, cy - size / 2 + by * unit), tints[i], shimmer, 4);
        }
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = W / 2;
        dc.FillRectangle(Brush(0x70000000), new Rect(0, 0, W, H));
        Text(dc, "A V A", new Point(cx, 48), 40, White, center: true);
        Logo(dc, 136, 104);
        Text(dc, "IN THE STYLE OF SEGA'S 1982 COLOUR VECTOR ARCADE GAME", new Point(cx, 214), 24, Dim, center: true);
        Text(dc, "COMMAND A SQUADRON OF UP TO SEVEN SHIPS. THEY MOVE TOGETHER AND FIRE TOGETHER.", new Point(cx, 262), 24, White, center: true);
        Text(dc, "CLEAR TWO WAVES ON THE FLAT, THEN FLY THE TUNNEL: SHOOT THE FIGHTERS, DODGE THE BARRIERS.", new Point(cx, 288), 24, White, center: true);
        Text(dc, "LINE UP UNDER THE MOTHER SHIP'S BAY TO DOCK FOR TWO MORE SHIPS. LOSE THEM ALL AND IT'S OVER.", new Point(cx, 314), 24, White, center: true);
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, H - 124, W, 124));
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR FIRE TO START", new Point(cx, H - 94), 44, Yellow, center: true);
        Text(dc, "LEFT/RIGHT, A/D, MOUSE OR WHEEL: SPIN THE SQUADRON    Z, SPACE, CTRL OR CLICK: FIRE (HOLD)", new Point(cx, H - 56), 21, 0xFFFFF0E0, center: true);
        Text(dc, "P: PAUSE   M: SOUND   ESC: QUIT        TOP SCORE " + Points(Engine.HighScores.TopScore), new Point(cx, H - 28), 21, Cyan, center: true);
    }

    private void Panel(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0x90000008), new Rect(0, 0, W, H));
        var r = new Rect(W / 2 - 330, 60, 660, 650);
        dc.FillRectangle(Brush(0xC0020418), r);
        Outline(dc, [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft], Cyan, 0.9, 2);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        Panel(dc);
        Text(dc, "SQUADRON LEADERS", new Point(cx, 112), 60, Yellow, center: true);
        Text(dc, "RANK NAME        SCORE   ROUND", new Point(cx, 178), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,12:N0}   {e.Stage,4}".Replace(",", " "), new Point(cx, 224 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 672), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = W / 2;
        Panel(dc);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, Points(g.Score), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    /// <summary>A rounded rectangle with a radius worked out in doubles.</summary>
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
