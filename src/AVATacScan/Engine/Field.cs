namespace AVATacScan.Engine;

/// <summary>
/// What the player asks for each step: the spinner turned (Spin, -1 to 1, right is +1), the guns, and with
/// <see cref="Steer"/> a place on the track (Target, -1 at the far left to 1 at the far right) for the
/// squadron's leader to fly to, the way the mouse steers.
/// </summary>
public readonly record struct Controls(double Spin = 0, bool Fire = false, bool Steer = false, double Target = 0);

/// <summary>
/// The measures of the game, and where things are on the 1024×768 screen.
/// <para>
/// The squadron flies along a track at the foot of the screen. Its place on the track is a number from -1 (far
/// left) to 1 (far right), the same in both kinds of play: on the flat it's a point on a wide shallow arc, the ships
/// nosing up the screen towards the arc's centre; in the tunnel it's a point on the near rim of a tube running into
/// the screen, the ships nosing towards the vanishing point.
/// </para>
/// </summary>
public static class Field
{
    public const double Width = 1024, Height = 768;
    public const double Step = 1.0 / 60;

    // ───────────────────────────── the squadron ─────────────────────────────

    public const int MaxShips = 7, StartShips = 4;
    /// <summary>How far apart two neighbouring ships are along the track, in track units.</summary>
    public const double SlotSpacing = 0.1;
    /// <summary>How fast the spinner (or the keys) carries the squadron along the track, in track units a second.</summary>
    public const double SpinSpeed = 1.15;
    /// <summary>How fast the squadron flies to the mouse: a spinner given a good hard turn.</summary>
    public const double SteerSpeed = 1.7;
    /// <summary>Time between volleys with the button held.</summary>
    public const double Reload = 0.24;

    /// <summary>
    /// Where each place in the formation sits: across (in slots, left negative) and back (in rows). The leader is in
    /// front in the middle; the wingmen pair off either side of it, a row further back each pair: an arrowhead.
    /// </summary>
    public static readonly (int Side, int Row)[] Slots = [(0, 0), (-1, 1), (1, 1), (-2, 2), (2, 2), (-3, 3), (3, 3)];

    /// <summary>The leftmost and rightmost slot a squadron of <paramref name="ships"/> fills.</summary>
    public static (int Left, int Right) Spread(int ships)
    {
        int l = 0, r = 0;
        for (int i = 0; i < Math.Min(ships, MaxShips); i++) { l = Math.Min(l, Slots[i].Side); r = Math.Max(r, Slots[i].Side); }
        return (l, r);
    }

    /// <summary>The farthest left and right the leader can go with every ship still on the track.</summary>
    public static (double Min, double Max) LeadRange(int ships)
    {
        var (l, r) = Spread(ships);
        return (-1 - l * SlotSpacing, 1 - r * SlotSpacing);
    }

    /// <summary>A ship's place on the track, from the leader's.</summary>
    public static double ShipPos(double lead, int slot) => lead + Slots[slot].Side * SlotSpacing;

    // ───────────────────────────── the flat ─────────────────────────────

    /// <summary>The flat track: an arc of a big circle whose centre is far above the screen.</summary>
    public const double ArcX = 512, ArcY = -560, ArcR = 1230, FlatSpan = 0.36;
    /// <summary>How far behind (down the screen) each row of the arrowhead flies on the flat.</summary>
    public const double FlatRow = 15;
    public const double ShotSpeed = 950, ShipRadius = 14;

    private static double FlatAngle(double pos) => Math.PI / 2 - pos * FlatSpan;

    /// <summary>A point on the flat track (or <paramref name="back"/> pixels behind it) and the way a ship there faces.</summary>
    public static (double X, double Y, double Dx, double Dy) Flat(double pos, double back = 0)
    {
        double a = FlatAngle(pos), c = Math.Cos(a), s = Math.Sin(a), r = ArcR + back;
        return (ArcX + c * r, ArcY + s * r, -c, -s);
    }

    /// <summary>Where a ship of the squadron is on the flat.</summary>
    public static (double X, double Y, double Dx, double Dy) FlatShip(double lead, int slot) =>
        Flat(ShipPos(lead, slot), Slots[slot].Row * FlatRow);

    /// <summary>The place on the flat track nearest a point on the screen (for the mouse).</summary>
    public static double FlatPosAt(double x, double y) => (Math.PI / 2 - Math.Atan2(y - ArcY, x - ArcX)) / FlatSpan;

    // ───────────────────────────── the tunnel ─────────────────────────────

    /// <summary>The vanishing point, and the scale: a ring of the tube one unit deep is <see cref="Focal"/> pixels round.</summary>
    public const double Cx = 512, Cy = 330, Focal = 320;
    /// <summary>How far round the near rim the track runs either side of the bottom, in radians.</summary>
    public const double TunnelSpan = 1.25;
    /// <summary>The depth the leader flies at; each row of the arrowhead is a little nearer.</summary>
    public const double ShipZ = 1.15, TunnelRow = 0.045;
    /// <summary>The far end of the tube, where everything comes from.</summary>
    public const double FarZ = 11;
    public const double TunnelShotSpeed = 15;
    /// <summary>How close (in track units) a shot or a ship must be to something to touch it, in the tunnel.</summary>
    public const double TunnelReach = 0.07;

    public static double TunnelAngle(double pos) => Math.PI / 2 - pos * TunnelSpan;

    /// <summary>A point on the tube's wall: its place round the tube (in track units) and its depth, on the screen.</summary>
    public static (double X, double Y, double Scale) Tube(double pos, double z, double radius = 1)
    {
        double a = TunnelAngle(pos), s = Focal / Math.Max(0.2, z);
        return (Cx + Math.Cos(a) * radius * s, Cy + Math.Sin(a) * radius * s, s / Focal);
    }

    /// <summary>A point given by its angle round the tube (radians, 0 is to the right, down is +π/2).</summary>
    public static (double X, double Y) TubeAt(double angle, double z, double radius = 1)
    {
        double s = Focal / Math.Max(0.2, z);
        return (Cx + Math.Cos(angle) * radius * s, Cy + Math.Sin(angle) * radius * s);
    }

    public static double ShipDepth(int slot) => ShipZ - Slots[slot].Row * TunnelRow;

    /// <summary>The place on the tunnel's track nearest a point on the screen (for the mouse).</summary>
    public static double TunnelPosAt(double x, double y)
    {
        // Below the vanishing point, the way round the tube the pointer is; above it, how far across.
        double a = y > Cy + 20
            ? Math.Atan2(y - Cy, x - Cx)
            : Math.Acos(Math.Clamp((x - Cx) / (Focal / ShipZ), -1, 1));
        return (Math.PI / 2 - a) / TunnelSpan;
    }

    // ───────────────────────────── the rounds ─────────────────────────────

    /// <summary>How much faster and fiercer everything is on a round (0 is the first).</summary>
    public static double Pace(int round) => Math.Min(2.2, 1 + 0.13 * round);

    /// <summary>How many enemies must be shot down to clear a flat wave.</summary>
    public static int Quota(int round, int wave) => Math.Min(32, 12 + 3 * round + 2 * wave);

    /// <summary>How long the tunnel run lasts before the mother ship comes, in seconds.</summary>
    public static double TunnelSeconds(int round) => Math.Min(36, 22 + 2 * round);

    public const int WavesPerRound = 2;
    /// <summary>Ships the mother ship gives for docking; past a full squadron each is points instead.</summary>
    public const int DockShips = 2, SpareShipPoints = 2000;
    public const double DockReach = 0.07;

    /// <summary>The first extra ship, and how often after that.</summary>
    public const int ExtraShipAt = 20000, ExtraShipEvery = 50000;
}
