namespace AVATacScan.Engine;

/// <summary>The enemies of the flat waves.</summary>
public enum Foe
{
    /// <summary>A saucer drifting down the screen, swaying, dropping shots.</summary>
    Saucer,
    /// <summary>Darts: they come in a string, swing round the top of the screen, then dive at the squadron.</summary>
    Dart,
    /// <summary>A spinning cross that bounces about the top of the screen and aims its fire. Takes two hits.</summary>
    Spinner,
    /// <summary>A bomber crossing the top of the screen, dropping bombs on whatever is under it.</summary>
    Bomber,
}

/// <summary>What comes down the tunnel.</summary>
public enum Threat
{
    /// <summary>A fighter flying out of the vanishing point, some of them spiralling, some firing.</summary>
    Fighter,
    /// <summary>A seeker: it turns round the tube towards the squadron as it comes.</summary>
    Seeker,
    /// <summary>A barrier: a band of force across part of the tube. Shots can't break it; fly round it.</summary>
    Barrier,
}

/// <summary>
/// One ship of the squadron. Its place in the arrowhead is its place in the list; when a ship is lost the ones
/// behind move up, sliding across to their new slots (<see cref="Side"/> and <see cref="Row"/> ease towards them).
/// </summary>
public sealed class Ship
{
    /// <summary>Where it is in the formation now: across in slots (left negative) and back in rows.</summary>
    public double Side, Row;
    /// <summary>Seconds since it joined the squadron (it flashes as it arrives).</summary>
    public double Age;
}

/// <summary>A shot from the squadron: on the flat it flies across the screen, in the tunnel down the tube.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy;
    /// <summary>In the tunnel: its place round the tube (track units) and its depth.</summary>
    public double Pos, Z;
    public bool Dead;
}

public sealed class Enemy
{
    public Foe Kind;
    public double X, Y, Vx, Vy;
    public double T, Fire, Spin;
    public int Hp, Mode, Index;
    /// <summary>Where it came on (the swing's centre, for darts), and which way round it swings.</summary>
    public double Ox, Oy, Dir;
    public double Flash;
    public bool Dead;

    public static double Radius(Foe kind) => kind switch { Foe.Bomber => 24, Foe.Spinner => 20, _ => 17 };
    public static int Points(Foe kind) => kind switch { Foe.Saucer => 100, Foe.Dart => 150, Foe.Bomber => 200, _ => 300 };
    public static int Toughness(Foe kind) => kind == Foe.Spinner ? 2 : 1;
}

/// <summary>Enemy fire on the flat.</summary>
public sealed class Bullet
{
    public double X, Y, Vx, Vy;
    /// <summary>A bomb (from a bomber) rather than a shot.</summary>
    public bool Bomb;
    public bool Dead;
}

/// <summary>Something coming down the tube at the squadron.</summary>
public sealed class Incoming
{
    public Threat Kind;
    /// <summary>Its place round the tube (track units); a barrier runs from <see cref="Pos"/> to <see cref="To"/>.</summary>
    public double Pos, To;
    public double Z, Vz, Vpos;
    public double T, Fire;
    public double Flash;
    public bool Dead;

    public static int Points(Threat kind) => kind == Threat.Seeker ? 400 : 250;
}

/// <summary>A fighter's bolt coming down the tube.</summary>
public sealed class Bolt
{
    public double Pos, Z, Vz;
    public bool Dead;
}

/// <summary>A burst of light on the screen, where something blew up.</summary>
public sealed class Blast
{
    public double X, Y, T, Size;
    public uint Colour;
    /// <summary>Its sparks' directions, so each burst is its own shape.</summary>
    public double[] Angles = [];
}

/// <summary>The mother ship, coming out of the tunnel to dock the squadron.</summary>
public sealed class MotherShip
{
    public double Z = Field.FarZ + 2;
    /// <summary>Where on the track its docking bay is: the leader must be under it as it arrives.</summary>
    public double Bay;
    public double T;
    /// <summary>Whether it has arrived, and whether the squadron made it in.</summary>
    public bool Arrived, Docked;
}

/// <summary>Everything in play: the squadron, its shots, the enemies and their fire, and the round.</summary>
public sealed class World
{
    public readonly List<Ship> Ships = [];
    /// <summary>The squadron leader's place on the track, -1 to 1.</summary>
    public double Lead;
    /// <summary>How fast the squadron was moving along the track last step (for the banking).</summary>
    public double LeadSpeed;
    public double Reload;

    public readonly List<Shot> Shots = [];
    public readonly List<Enemy> Enemies = [];
    public readonly List<Bullet> Bullets = [];
    public readonly List<Incoming> Incoming = [];
    public readonly List<Bolt> Bolts = [];
    public readonly List<Blast> Blasts = [];
    public MotherShip? Mother;

    /// <summary>The round (0 is the first) and the flat wave within it.</summary>
    public int Round, Wave;
    /// <summary>On a flat wave: how many have come on, and how many have been shot down.</summary>
    public int Spawned, Killed;
    public double SpawnClock;
    /// <summary>Down the tunnel (rather than on the flat).</summary>
    public bool Tunnel;
    /// <summary>In the tunnel: how long the squadron has been flying down it, and how far the rings have scrolled.</summary>
    public double Flown, Scroll;
    public double BarrierClock;

    public int Score;
    public int NextExtra = Field.ExtraShipAt;
    public int Lost;
    public double Clock;
    public Random Rng = new(1);
    /// <summary>Sounds asked for this step, played (or not) by the engine.</summary>
    public readonly List<Sfx> Heard = [];

    public double Pace => Field.Pace(Round);
    public int Quota => Field.Quota(Round, Wave);
    public bool WaveDone => Killed >= Quota;
    public bool Wiped => Ships.Count == 0;
}
