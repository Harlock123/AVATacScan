using AVATacScan.Engine;

namespace AVATacScan.Tests;

/// <summary>Worlds and games set up for the tests, with anything saved kept in a temporary folder.</summary>
internal static class Harness
{
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avatacscan-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine Game(int seed = 1) => new(HighScoreTable.Load(TempFolder()), seed) { Silent = true };

    /// <summary>A flat wave with nothing on it yet (and nothing coming unless <see cref="Sim.StepFlat"/> is asked to spawn),
    /// <paramref name="ships"/> ships in their slots, the leader in the middle.</summary>
    public static World Flat(int ships = Field.StartShips, int round = 0)
    {
        var w = new World();
        Sim.Start(w, 1);
        if (round > 0) Sim.Begin(w, round);
        w.Ships.Clear();
        for (int i = 0; i < ships; i++) w.Ships.Add(new Ship { Side = Field.Slots[i].Side, Row = Field.Slots[i].Row, Age = 5 });
        w.Lead = 0;
        w.Heard.Clear();
        return w;
    }

    /// <summary>The same, down the tunnel.</summary>
    public static World Tunnel(int ships = Field.StartShips, int round = 0)
    {
        var w = Flat(ships, round);
        Sim.EnterTunnel(w);
        return w;
    }

    public static void Run(World w, int steps, Controls c = default, bool spawn = false)
    {
        for (int i = 0; i < steps; i++)
        {
            if (w.Tunnel) Sim.StepTunnel(w, c, spawn);
            else Sim.StepFlat(w, c, spawn);
        }
    }

    /// <summary>An enemy of the flat where it's put (darts, spinners and bombers stay there).</summary>
    public static Enemy Still(World w, Foe kind, double x, double y)
    {
        // A dart already diving, at no speed, stays put; so do spinners and bombers given no speed.
        var e = new Enemy { Kind = kind, X = x, Y = y, Ox = x, Hp = Enemy.Toughness(kind), Fire = 1e9, Mode = kind == Foe.Dart ? 2 : 0 };
        w.Enemies.Add(e);
        return e;
    }

    /// <summary>Something coming down the tube.</summary>
    public static Incoming Coming(World w, Threat kind, double pos, double z, double vz = -3, double to = 0)
    {
        var e = new Incoming { Kind = kind, Pos = pos, To = to, Z = z, Vz = vz, Fire = 1e9 };
        w.Incoming.Add(e);
        return e;
    }

    /// <summary>Where a ship of the squadron is on the flat.</summary>
    public static (double X, double Y) At(World w, int i)
    {
        var (x, y, _, _) = Sim.FlatOf(w, w.Ships[i]);
        return (x, y);
    }
}
