namespace AVATacScan.Engine;

/// <summary>
/// The rules, one 60th of a second at a time: the squadron moving and firing together, the flat waves, the tunnel
/// and the mother ship. The engine decides which of these runs when.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;

    /// <summary>A new game: a fresh squadron in the middle of the track, on the flat, round 1.</summary>
    public static void Start(World w, int seed)
    {
        w.Rng = new Random(seed);
        w.Ships.Clear();
        w.Lead = 0;
        w.Score = 0;
        w.NextExtra = Field.ExtraShipAt;
        AddShips(w, Field.StartShips, quiet: true);
        foreach (var s in w.Ships) s.Row -= 3;
        Begin(w, 0);
    }

    /// <summary>The start of a round: back on the flat, its first wave to come.</summary>
    public static void Begin(World w, int round)
    {
        w.Round = round;
        w.Wave = 0;
        w.Tunnel = false;
        w.Mother = null;
        NextWave(w);
        w.Bullets.Clear();
    }

    /// <summary>A flat wave's start: none of its enemies on yet.</summary>
    public static void NextWave(World w)
    {
        w.Spawned = w.Killed = 0;
        w.SpawnClock = 0.6;
        w.Enemies.Clear();
        w.Shots.Clear();
    }

    /// <summary>Into the tunnel: the flat's enemies and fire are left behind.</summary>
    public static void EnterTunnel(World w)
    {
        w.Tunnel = true;
        w.Flown = 0;
        w.SpawnClock = 1.2;
        w.BarrierClock = 5;
        w.Enemies.Clear();
        w.Bullets.Clear();
        w.Shots.Clear();
        w.Incoming.Clear();
        w.Bolts.Clear();
    }

    /// <summary>Ships join the squadron, sliding up from behind into the empty slots. Returns how many didn't fit.</summary>
    public static int AddShips(World w, int n, bool quiet = false)
    {
        int spare = 0;
        for (int i = 0; i < n; i++)
        {
            int slot = w.Ships.Count;
            if (slot >= Field.MaxShips) { spare++; continue; }
            w.Ships.Add(new Ship { Side = Field.Slots[slot].Side, Row = Field.Slots[slot].Row + 2.5 });
        }
        if (!quiet && spare < n) w.Heard.Add(Sfx.ExtraShip);
        return spare;
    }

    public static void AddScore(World w, int points)
    {
        w.Score += points;
        while (w.Score >= w.NextExtra)
        {
            w.NextExtra += Field.ExtraShipEvery;
            AddShips(w, 1);
        }
    }

    // ───────────────────────────── the squadron ─────────────────────────────

    /// <summary>A ship's place on the track.</summary>
    public static double PosOf(World w, Ship s) => w.Lead + s.Side * Field.SlotSpacing;

    /// <summary>Where a ship is on the flat, and the way it faces.</summary>
    public static (double X, double Y, double Dx, double Dy) FlatOf(World w, Ship s) => Field.Flat(PosOf(w, s), s.Row * Field.FlatRow);

    /// <summary>A ship's depth in the tunnel.</summary>
    public static double DepthOf(Ship s) => Field.ShipZ - s.Row * Field.TunnelRow;

    /// <summary>The spinner, the keys or the mouse move the whole squadron along the track; the guns fire together.</summary>
    public static void Fly(World w, Controls c, bool canFire = true)
    {
        var (min, max) = Field.LeadRange(w.Ships.Count);
        double v = c.Steer
            ? Math.Clamp((Math.Clamp(c.Target, min, max) - w.Lead) / Dt, -Field.SteerSpeed, Field.SteerSpeed)
            : Math.Clamp(c.Spin, -1, 1) * Field.SpinSpeed;
        double was = w.Lead;
        w.Lead = Math.Clamp(w.Lead + v * Dt, min, max);
        w.LeadSpeed = (w.Lead - was) / Dt;
        for (int i = 0; i < w.Ships.Count; i++)
        {
            var s = w.Ships[i];
            var (side, row) = Field.Slots[i];
            s.Side += Math.Clamp(side - s.Side, -4 * Dt, 4 * Dt);
            s.Row += Math.Clamp(row - s.Row, -5 * Dt, 5 * Dt);
            s.Age += Dt;
        }
        w.Reload -= Dt;
        if (canFire && c.Fire && w.Reload <= 0 && w.Ships.Count > 0) Volley(w);
    }

    /// <summary>Every ship fires at once.</summary>
    private static void Volley(World w)
    {
        w.Reload = Field.Reload;
        foreach (var s in w.Ships)
        {
            if (w.Tunnel) w.Shots.Add(new Shot { Pos = PosOf(w, s), Z = DepthOf(s) + 0.05 });
            else
            {
                var (x, y, dx, dy) = FlatOf(w, s);
                w.Shots.Add(new Shot { X = x + dx * 16, Y = y + dy * 16, Vx = dx * Field.ShotSpeed, Vy = dy * Field.ShotSpeed });
            }
        }
        w.Heard.Add(Sfx.Fire);
    }

    /// <summary>A ship of the squadron is gone, and the rest close up.</summary>
    public static void LoseShip(World w, Ship s)
    {
        double x, y;
        if (w.Tunnel) (x, y, _) = Field.Tube(PosOf(w, s), DepthOf(s));
        else (x, y, _, _) = FlatOf(w, s);
        Burst(w, x, y, 46, 0xFFFFFFFF, 18);
        Burst(w, x, y, 30, 0xFF6AE8FF, 10);
        w.Ships.Remove(s);
        w.Lost++;
        w.Heard.Add(Sfx.ShipLost);
    }

    public static void Burst(World w, double x, double y, double size, uint colour, int sparks = 12)
    {
        var angles = new double[sparks];
        for (int i = 0; i < sparks; i++) angles[i] = (i + w.Rng.NextDouble() * 0.8) * Math.PI * 2 / sparks;
        w.Blasts.Add(new Blast { X = x, Y = y, Size = size, Colour = colour, Angles = angles });
    }

    private static void Common(World w)
    {
        w.Clock += Dt;
        foreach (var b in w.Blasts) b.T += Dt;
        w.Blasts.RemoveAll(b => b.T > 0.8);
    }

    // ───────────────────────────── the flat ─────────────────────────────

    /// <summary>What comes on a flat wave: more kinds as the rounds go by.</summary>
    public static Foe[] Mix(int round, int wave) => (round, wave) switch
    {
        (0, 0) => [Foe.Saucer, Foe.Dart],
        (0, _) => [Foe.Dart, Foe.Spinner],
        (1, 0) => [Foe.Saucer, Foe.Dart, Foe.Bomber],
        (1, _) => [Foe.Dart, Foe.Spinner, Foe.Bomber],
        _ => [Foe.Saucer, Foe.Dart, Foe.Spinner, Foe.Bomber],
    };

    /// <summary>How many enemies can be on the screen at once.</summary>
    public static int Crowd(int round) => Math.Min(10, 5 + round);

    /// <summary>A step on the flat: the squadron, its shots, the wave's enemies (new ones only with <paramref name="spawn"/>) and their fire.</summary>
    public static void StepFlat(World w, Controls c, bool spawn = true)
    {
        Common(w);
        Fly(w, c);
        if (spawn) Spawn(w);
        foreach (var e in w.Enemies) Move(w, e);
        foreach (var b in w.Bullets)
        {
            b.X += b.Vx * Dt;
            b.Y += b.Vy * Dt;
            if (b.Y > Field.Height + 20 || b.Y < -40 || b.X < -40 || b.X > Field.Width + 40) b.Dead = true;
        }
        foreach (var s in w.Shots)
        {
            double x0 = s.X, y0 = s.Y;
            s.X += s.Vx * Dt;
            s.Y += s.Vy * Dt;
            if (s.Y < -20 || s.X < -20 || s.X > Field.Width + 20) { s.Dead = true; continue; }
            foreach (var e in w.Enemies)
            {
                if (e.Dead || SegmentDistance(e.X, e.Y, x0, y0, s.X, s.Y) > Enemy.Radius(e.Kind)) continue;
                s.Dead = true;
                Hit(w, e);
                break;
            }
        }
        // The squadron hit: by fire, or by an enemy flying into it.
        for (int i = w.Ships.Count - 1; i >= 0; i--)
        {
            var ship = w.Ships[i];
            var (x, y, _, _) = FlatOf(w, ship);
            bool gone = false;
            foreach (var b in w.Bullets)
            {
                if (b.Dead || Math.Abs(b.X - x) > 30 || double.Hypot(b.X - x, b.Y - y) > Field.ShipRadius + (b.Bomb ? 6 : 3)) continue;
                b.Dead = true;
                gone = true;
                break;
            }
            if (!gone)
                foreach (var e in w.Enemies)
                {
                    if (e.Dead || double.Hypot(e.X - x, e.Y - y) > Field.ShipRadius + Enemy.Radius(e.Kind) * 0.8) continue;
                    Kill(w, e);
                    gone = true;
                    break;
                }
            if (gone) LoseShip(w, ship);
        }
        w.Shots.RemoveAll(s => s.Dead);
        w.Bullets.RemoveAll(b => b.Dead);
        w.Enemies.RemoveAll(e => e.Dead);
    }

    /// <summary>How near a point comes to a line segment.</summary>
    public static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        double t = l2 <= 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
        return double.Hypot(px - (ax + dx * t), py - (ay + dy * t));
    }

    private static void Hit(World w, Enemy e)
    {
        e.Hp--;
        e.Flash = 0.15;
        if (e.Hp <= 0) Kill(w, e);
        else w.Heard.Add(Sfx.Clang);
    }

    private static void Kill(World w, Enemy e)
    {
        if (e.Dead) return;
        e.Dead = true;
        w.Killed++;
        AddScore(w, Enemy.Points(e.Kind));
        Burst(w, e.X, e.Y, Enemy.Radius(e.Kind) * 2, Colour(e.Kind));
        w.Heard.Add(Sfx.Blast);
    }

    public static uint Colour(Foe kind) => kind switch
    {
        Foe.Saucer => 0xFF4AF0FF,
        Foe.Dart => 0xFFFFE04A,
        Foe.Spinner => 0xFFFF4AD8,
        _ => 0xFFFF8A3A,
    };

    private static void Spawn(World w)
    {
        w.SpawnClock -= Dt;
        int left = w.Quota - w.Spawned;
        if (left <= 0 || w.SpawnClock > 0 || w.Enemies.Count >= Crowd(w.Round)) return;
        var mix = Mix(w.Round, w.Wave);
        var kind = mix[w.Rng.Next(mix.Length)];
        double pace = w.Pace;
        var rng = w.Rng;
        switch (kind)
        {
            case Foe.Dart:
            {
                // A string of them, one behind the other, swinging round the same way.
                int n = Math.Min(left, 3 + Math.Min(2, w.Round / 2));
                double dir = rng.Next(2) == 0 ? -1 : 1, ox = 260 + rng.NextDouble() * 504, oy = 180 + rng.NextDouble() * 90;
                for (int i = 0; i < n; i++)
                    w.Enemies.Add(new Enemy { Kind = kind, Hp = 1, Dir = dir, Ox = ox, Oy = oy, X = ox + dir * 100, Y = -30 - i * 55, Index = i });
                w.Spawned += n;
                w.SpawnClock = 2.6 / pace;
                return;
            }
            case Foe.Saucer:
            {
                double ox = 140 + rng.NextDouble() * 744;
                w.Enemies.Add(new Enemy { Kind = kind, Hp = 1, Ox = ox, X = ox, Y = -30, Spin = rng.NextDouble() * 6, Fire = 1 + rng.NextDouble() * 2 });
                break;
            }
            case Foe.Spinner:
            {
                double a = Math.PI / 2 + (rng.NextDouble() - 0.5) * 1.6;
                w.Enemies.Add(new Enemy { Kind = kind, Hp = Enemy.Toughness(kind), X = 120 + rng.NextDouble() * 784, Y = -30,
                    Vx = Math.Cos(a) * 140 * pace, Vy = Math.Sin(a) * 140 * pace, Fire = 1.5 + rng.NextDouble() });
                break;
            }
            case Foe.Bomber:
            {
                double dir = rng.Next(2) == 0 ? -1 : 1;
                w.Enemies.Add(new Enemy { Kind = kind, Hp = 1, Dir = dir, X = dir > 0 ? -40 : Field.Width + 40, Y = 90 + rng.NextDouble() * 120, Vx = dir * 150 * pace });
                break;
            }
        }
        w.Spawned++;
        w.SpawnClock = (0.9 + rng.NextDouble() * 0.5) / pace;
    }

    /// <summary>A ship of the squadron to aim at: the nearest across to <paramref name="x"/>.</summary>
    private static (double X, double Y)? Nearest(World w, double x)
    {
        (double X, double Y)? best = null;
        foreach (var s in w.Ships)
        {
            var (sx, sy, _, _) = FlatOf(w, s);
            if (best == null || Math.Abs(sx - x) < Math.Abs(best.Value.X - x)) best = (sx, sy);
        }
        return best;
    }

    private static void Aim(World w, Enemy e, double speed)
    {
        if (Nearest(w, e.X) is not { } t) return;
        double dx = t.X - e.X, dy = t.Y - e.Y, d = Math.Max(1, double.Hypot(dx, dy));
        w.Bullets.Add(new Bullet { X = e.X, Y = e.Y + 10, Vx = dx / d * speed, Vy = dy / d * speed });
        w.Heard.Add(Sfx.EnemyFire);
    }

    private static void Move(World w, Enemy e)
    {
        double pace = w.Pace;
        e.T += Dt;
        e.Flash -= Dt;
        e.Fire -= Dt;
        var rng = w.Rng;
        switch (e.Kind)
        {
            case Foe.Saucer:
                e.Y += 72 * pace * Dt;
                e.X = e.Ox + 120 * Math.Sin(e.T * 1.3 + e.Spin);
                (e.Vx, e.Vy) = (120 * 1.3 * Math.Cos(e.T * 1.3 + e.Spin), 72 * pace);
                if (e.Fire <= 0 && e.Y is > 30 and < 470)
                {
                    e.Fire = (2.2 + rng.NextDouble() * 1.8) / pace;
                    w.Bullets.Add(new Bullet { X = e.X, Y = e.Y + 10, Vy = 240 * pace });
                    w.Heard.Add(Sfx.EnemyFire);
                }
                if (e.Y > Field.Height + 30) { e.Y = -30; e.Ox = 140 + rng.NextDouble() * 744; }
                break;
            case Foe.Dart:
                const double r = 100;
                if (e.Mode == 0)
                {
                    // Down the side of its swing, then into it.
                    (e.Vx, e.Vy) = (0, 300 * pace);
                    e.Y += e.Vy * Dt;
                    if (e.Y >= e.Oy) { e.Mode = 1; e.Spin = e.Dir > 0 ? 0 : Math.PI; e.T = 0; }
                }
                else if (e.Mode == 1)
                {
                    double was = e.Spin;
                    e.Spin += e.Dir * 2.6 * pace * Dt;
                    e.X = e.Ox + Math.Cos(e.Spin) * r;
                    e.Y = e.Oy + Math.Sin(e.Spin) * r;
                    e.Vx = (Math.Cos(e.Spin) - Math.Cos(was)) * r / Dt;
                    e.Vy = (Math.Sin(e.Spin) - Math.Sin(was)) * r / Dt;
                    if (e.T * 2.6 * pace > Math.PI * 2.25)
                    {
                        // Round once and a quarter: now the dive, at a ship.
                        e.Mode = 2;
                        e.T = 0;
                        double speed = 330 * pace;
                        if (Nearest(w, e.X) is { } t)
                        {
                            double dx = t.X - e.X, dy = t.Y - e.Y, d = Math.Max(1, double.Hypot(dx, dy));
                            (e.Vx, e.Vy) = (dx / d * speed, dy / d * speed);
                        }
                        else (e.Vx, e.Vy) = (0, speed);
                        if (w.Round > 0 && rng.NextDouble() < 0.5) Aim(w, e, 260 * pace);
                    }
                }
                else
                {
                    e.X += e.Vx * Dt;
                    e.Y += e.Vy * Dt;
                    if (e.Y > Field.Height + 30 || e.X < -60 || e.X > Field.Width + 60)
                    {
                        // Round again from the top.
                        e.Mode = 0;
                        e.Ox = 260 + rng.NextDouble() * 504;
                        e.X = e.Ox + e.Dir * r;
                        e.Y = -30 - e.Index * 55;
                    }
                }
                break;
            case Foe.Spinner:
                e.Spin += 5 * Dt;
                e.X += e.Vx * Dt;
                e.Y += e.Vy * Dt;
                if (e.X < 50 && e.Vx < 0 || e.X > Field.Width - 50 && e.Vx > 0) e.Vx = -e.Vx;
                if (e.Y > 380 && e.Vy > 0 || e.Y < 50 && e.Vy < 0 && e.T > 2) e.Vy = -e.Vy;
                if (e.Fire <= 0 && e.Y > 30)
                {
                    e.Fire = (1.8 + rng.NextDouble()) / pace;
                    Aim(w, e, 250 * pace);
                }
                break;
            case Foe.Bomber:
                e.X += e.Vx * Dt;
                if (e.Fire <= 0 && Nearest(w, e.X) is { } under && Math.Abs(under.X - e.X) < 24)
                {
                    e.Fire = 0.9 / pace;
                    w.Bullets.Add(new Bullet { X = e.X, Y = e.Y + 14, Vy = 210 * pace, Bomb = true });
                    w.Heard.Add(Sfx.Bomb);
                }
                if (e.X > Field.Width + 50 || e.X < -50)
                {
                    e.Dir = -e.Dir;
                    e.Vx = -e.Vx;
                    e.Y = 90 + rng.NextDouble() * 120;
                }
                break;
        }
    }

    // ───────────────────────────── the tunnel ─────────────────────────────

    /// <summary>How fast things come down the tube on a round, in depth a second.</summary>
    public static double Closing(World w) => 3.2 * Math.Pow(w.Pace, 0.8);

    /// <summary>A step in the tunnel: the squadron, its shots, and whatever comes at it (new ones only with <paramref name="spawn"/>).</summary>
    public static void StepTunnel(World w, Controls c, bool spawn = true)
    {
        Common(w);
        Fly(w, c);
        w.Flown += Dt;
        w.Scroll += Dt * 2.4 * Math.Pow(w.Pace, 0.5);
        if (spawn) SpawnIncoming(w);
        double lead = w.Lead;
        foreach (var e in w.Incoming)
        {
            e.T += Dt;
            e.Flash -= Dt;
            e.Z += e.Vz * Dt;
            if (e.Kind == Threat.Seeker) e.Pos += Math.Clamp(lead - e.Pos, -e.Vpos * Dt, e.Vpos * Dt);
            else e.Pos += e.Vpos * Dt;
            if (e.Kind != Threat.Barrier)
            {
                e.Fire -= Dt;
                if (e.Fire <= 0 && e.Z is > 3 and < 9)
                {
                    e.Fire = (1.6 + w.Rng.NextDouble() * 1.6) / w.Pace;
                    w.Bolts.Add(new Bolt { Pos = e.Pos, Z = e.Z - 0.2, Vz = e.Vz * 2 });
                    w.Heard.Add(Sfx.EnemyFire);
                }
            }
        }
        foreach (var b in w.Bolts) b.Z += b.Vz * Dt;
        foreach (var s in w.Shots)
        {
            double z0 = s.Z;
            s.Z += Field.TunnelShotSpeed * Dt;
            if (s.Z > Field.FarZ) { s.Dead = true; continue; }
            foreach (var e in w.Incoming)
            {
                if (e.Dead || e.Z < z0 - 0.15 || e.Z > s.Z + 0.15) continue;
                if (e.Kind == Threat.Barrier)
                {
                    if (s.Pos < e.Pos - 0.01 || s.Pos > e.To + 0.01) continue;
                    // Shots spend themselves on a barrier.
                    s.Dead = true;
                    if (e.Flash <= 0) w.Heard.Add(Sfx.Clang);
                    e.Flash = 0.12;
                    break;
                }
                if (Math.Abs(s.Pos - e.Pos) > Field.TunnelReach + 0.02) continue;
                s.Dead = true;
                KillIncoming(w, e);
                break;
            }
        }
        // What reaches the squadron's depth: a ship under it is lost.
        for (int i = w.Ships.Count - 1; i >= 0; i--)
        {
            var ship = w.Ships[i];
            double pos = PosOf(w, ship), d = DepthOf(ship);
            bool gone = false;
            foreach (var e in w.Incoming)
            {
                if (e.Dead || e.Z > d || e.Z - e.Vz * Dt < d) continue;
                if (e.Kind == Threat.Barrier ? pos > e.Pos - 0.035 && pos < e.To + 0.035 : Math.Abs(pos - e.Pos) < Field.TunnelReach)
                {
                    gone = true;
                    if (e.Kind != Threat.Barrier) KillIncoming(w, e, points: false);
                    break;
                }
            }
            if (!gone)
                foreach (var b in w.Bolts)
                {
                    if (b.Dead || b.Z > d || b.Z - b.Vz * Dt < d || Math.Abs(pos - b.Pos) > Field.TunnelReach * 0.8) continue;
                    b.Dead = true;
                    gone = true;
                    break;
                }
            if (gone) LoseShip(w, ship);
        }
        w.Shots.RemoveAll(s => s.Dead);
        w.Incoming.RemoveAll(e => e.Dead || e.Z < 0.45);
        w.Bolts.RemoveAll(b => b.Dead || b.Z < 0.45);
        if (w.Mother is { } m) StepMother(w, m);
    }

    private static void KillIncoming(World w, Incoming e, bool points = true)
    {
        e.Dead = true;
        if (points) AddScore(w, Incoming.Points(e.Kind));
        w.Killed++;
        var (x, y, s) = Field.Tube(e.Pos, e.Z);
        Burst(w, x, y, 60 * s, e.Kind == Threat.Seeker ? 0xFFFF4AD8 : 0xFF7AFF6A);
        w.Heard.Add(Sfx.Blast);
    }

    private static void SpawnIncoming(World w)
    {
        var rng = w.Rng;
        double pace = w.Pace, closing = Closing(w);
        w.SpawnClock -= Dt;
        if (w.SpawnClock <= 0)
        {
            w.SpawnClock = (0.55 + rng.NextDouble() * 0.5) / pace;
            bool seeker = w.Round > 0 && rng.NextDouble() < 0.25;
            var e = new Incoming
            {
                Kind = seeker ? Threat.Seeker : Threat.Fighter,
                Pos = (rng.NextDouble() * 2 - 1) * 1.05,
                Z = Field.FarZ,
                Vz = -closing * (0.9 + rng.NextDouble() * 0.3),
                Fire = (1 + rng.NextDouble() * 2) / pace,
            };
            if (seeker) e.Vpos = 0.3 * pace;
            else if (rng.NextDouble() < 0.35) e.Vpos = (rng.Next(2) == 0 ? -1 : 1) * (0.12 + rng.NextDouble() * 0.2);
            w.Incoming.Add(e);
        }
        w.BarrierClock -= Dt;
        if (w.BarrierClock <= 0)
        {
            w.BarrierClock = (3.6 + rng.NextDouble() * 2) / pace;
            // A band across part of the tube, always leaving a gap wide enough for a full squadron on one side.
            double width = 0.35 + rng.NextDouble() * (0.3 + Math.Min(0.15, w.Round * 0.05));
            const double gap = 0.85;
            double from = rng.Next(2) == 0
                ? -1 + gap + rng.NextDouble() * (1.1 - width - (-1 + gap))
                : -1.1 + rng.NextDouble() * (1 - gap - width - -1.1);
            w.Incoming.Add(new Incoming { Kind = Threat.Barrier, Pos = from, To = from + width, Z = Field.FarZ, Vz = -closing * 0.85 });
        }
    }

    // ───────────────────────────── the mother ship ─────────────────────────────

    /// <summary>How long the mother ship takes to come down the tunnel.</summary>
    public const double MotherSeconds = 7;

    /// <summary>The mother ship sets out from the far end, its bay somewhere the squadron can reach.</summary>
    public static MotherShip CallMother(World w)
    {
        var (min, max) = Field.LeadRange(Math.Max(1, w.Ships.Count));
        double bay = (w.Rng.NextDouble() * 2 - 1) * 0.75;
        // Never right where the squadron already is.
        if (Math.Abs(bay - w.Lead) < 0.3) bay = w.Lead > 0 ? bay - 0.6 : bay + 0.6;
        w.Mother = new MotherShip { Bay = Math.Clamp(bay, Math.Max(min, -0.85), Math.Min(max, 0.85)) };
        w.Heard.Add(Sfx.MotherShip);
        return w.Mother;
    }

    /// <summary>Where the mother ship stops: just beyond the squadron.</summary>
    public const double MotherStop = Field.ShipZ + 0.45;

    private static void StepMother(World w, MotherShip m)
    {
        m.T += Dt;
        if (m.Arrived) return;
        m.Z = Math.Max(MotherStop, m.Z - (Field.FarZ + 2 - MotherStop) / MotherSeconds * Dt);
        if (m.Z <= MotherStop)
        {
            m.Arrived = true;
            m.Docked = w.Ships.Count > 0 && Math.Abs(w.Lead - m.Bay) < Field.DockReach;
        }
    }

    /// <summary>The warp between the flat and the tunnel: the squadron can move, nothing else happens.</summary>
    public static void StepWarp(World w, Controls c)
    {
        Common(w);
        Fly(w, c, canFire: false);
        w.Scroll += Dt * 6;
    }
}
