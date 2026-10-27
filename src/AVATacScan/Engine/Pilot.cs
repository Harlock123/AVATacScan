namespace AVATacScan.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. Each step it looks along the track for the best place
/// for the squadron's leader: somewhere none of its ships will be hit on the way there or once there (by fire, by a
/// diving enemy, by a barrier coming down the tunnel), and from where the volley will meet the most enemies. It
/// steers there the way the mouse does and keeps the guns going. When the mother ship comes, it lines up on the bay.
/// </summary>
public sealed class Pilot
{
    public string Doing { get; private set; } = "";

    public void Reset() => Doing = "";

    /// <summary>How far ahead (in seconds) it watches what's coming.</summary>
    private const double Lookahead = 1.3, BarrierLookahead = 2.6;
    private const double Grid = 0.04;

    private readonly record struct Mover(double X, double Y, double Vx, double Vy, double R);

    public Controls Decide(GameEngine g)
    {
        var w = g.World;
        if (g.State is not (GameState.Playing or GameState.Attract) || w.Ships.Count == 0) return default;
        var (min, max) = Field.LeadRange(w.Ships.Count);
        Func<double, double> cost = w.Tunnel ? TunnelCost(w) : FlatCost(w);
        double best = w.Lead, bestCost = double.MaxValue;
        for (double p = min; p <= max + 1e-9; p += Grid)
        {
            double c = cost(p);
            if (c < bestCost) { bestCost = c; best = p; }
        }
        // The exact spot it's in now, too: no dithering about between grid points.
        double here = cost(w.Lead);
        if (here <= bestCost + 0.5) best = w.Lead;
        Doing = bestCost > 50 ? "dodging" : w.Mother != null ? "docking" : "aiming";
        return new Controls(0, true, true, best);
    }

    /// <summary>Where the leader will be after <paramref name="t"/> seconds on its way to <paramref name="to"/>.</summary>
    private static double LeadAt(double from, double to, double t) =>
        from + Math.Clamp(to - from, -Field.SteerSpeed * t, Field.SteerSpeed * t);

    // ───────────────────────────── the flat ─────────────────────────────

    private static Func<double, double> FlatCost(World w)
    {
        var threats = new List<Mover>();
        foreach (var b in w.Bullets) threats.Add(new Mover(b.X, b.Y, b.Vx, b.Vy, b.Bomb ? 8 : 5));
        foreach (var e in w.Enemies) threats.Add(new Mover(e.X, e.Y, e.Vx, e.Vy, Enemy.Radius(e.Kind) * 0.8));
        // Only what could reach the squadron's height in time.
        threats.RemoveAll(t => t.Y + Math.Max(0, t.Vy) * Lookahead + t.R < 520 || t.Y > Field.Height + 10);
        var ships = w.Ships.Select(s => (s.Side, s.Row)).ToArray();
        double lead = w.Lead;
        return p =>
        {
            double cost = Math.Abs(p - lead) * 3;
            for (double t = 0; t <= Lookahead + 1e-9; t += 0.1)
            {
                double l = LeadAt(lead, p, t);
                foreach (var (side, row) in ships)
                {
                    var (x, y, _, _) = Field.Flat(l + side * Field.SlotSpacing, row * Field.FlatRow);
                    foreach (var m in threats)
                    {
                        double tx = m.X + m.Vx * t, ty = m.Y + m.Vy * t;
                        double reach = Field.ShipRadius + m.R + 10;
                        if (Math.Abs(tx - x) < reach && Math.Abs(ty - y) < reach) cost += 120 * (Lookahead + 0.3 - t);
                    }
                }
            }
            // Lined up under enemies: the volley will meet them.
            foreach (var e in w.Enemies)
            {
                if (e.Y < 0) continue;
                foreach (var (side, row) in ships)
                {
                    var (x, y, dx, dy) = Field.Flat(p + side * Field.SlotSpacing, row * Field.FlatRow);
                    double tau = (y - e.Y) / (Field.ShotSpeed * -dy);
                    double sx = x + dx / dy * (e.Y - y);
                    if (Math.Abs(sx - (e.X + e.Vx * tau)) < Enemy.Radius(e.Kind) * 0.8) { cost -= 6 + e.Y / 80; break; }
                }
            }
            return cost;
        };
    }

    // ───────────────────────────── the tunnel ─────────────────────────────

    private static Func<double, double> TunnelCost(World w)
    {
        var ships = w.Ships.Select(s => (s.Side, Depth: Sim.DepthOf(s))).ToArray();
        double lead = w.Lead;
        var mother = w.Mother;
        return p =>
        {
            double cost = Math.Abs(p - lead) * 2;
            if (mother is { Arrived: false }) cost += Math.Abs(p - mother.Bay) * 300;
            foreach (var (side, depth) in ships)
            {
                double off = side * Field.SlotSpacing;
                foreach (var e in w.Incoming)
                {
                    if (e.Vz >= 0) continue;
                    double t = (e.Z - depth) / -e.Vz;
                    if (t < -0.05) continue;
                    double ship = LeadAt(lead, p, Math.Max(0, t)) + off;
                    if (e.Kind == Threat.Barrier)
                    {
                        if (t > BarrierLookahead) continue;
                        if (ship > e.Pos - 0.1 && ship < e.To + 0.1) cost += 400 * (BarrierLookahead + 0.3 - t);
                        // Already across the barrier's line in good time, or not: where it ends up must be clear too.
                        double end = p + off;
                        if (end > e.Pos - 0.1 && end < e.To + 0.1) cost += 200;
                        continue;
                    }
                    if (t > Lookahead) continue;
                    double at = e.Kind == Threat.Seeker ? e.Pos + Math.Clamp(p - e.Pos, -e.Vpos * Math.Max(0, t), e.Vpos * Math.Max(0, t)) : e.Pos + e.Vpos * t;
                    if (Math.Abs(ship - at) < Field.TunnelReach + 0.05) cost += 120 * (Lookahead + 0.3 - t);
                }
                foreach (var b in w.Bolts)
                {
                    double t = (b.Z - depth) / -b.Vz;
                    if (t < -0.05 || t > Lookahead) continue;
                    double ship = LeadAt(lead, p, Math.Max(0, t)) + off;
                    if (Math.Abs(ship - b.Pos) < Field.TunnelReach + 0.04) cost += 120 * (Lookahead + 0.3 - t);
                }
            }
            // Lined up with what's coming: the volley will meet it.
            foreach (var e in w.Incoming)
            {
                if (e.Kind == Threat.Barrier || e.Z < Field.ShipZ + 0.6) continue;
                double tau = (e.Z - Field.ShipZ) / (Field.TunnelShotSpeed - e.Vz);
                double at = e.Pos + e.Vpos * tau;
                foreach (var (side, _) in ships)
                    if (Math.Abs(p + side * Field.SlotSpacing - at) < Field.TunnelReach) { cost -= 6 + (Field.FarZ - e.Z) * 0.8; break; }
            }
            return cost;
        };
    }
}
