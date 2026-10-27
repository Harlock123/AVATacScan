using AVATacScan.Engine;

namespace AVATacScan.Tests;

/// <summary>The computer pilot: it has to be good enough to show the game off in the demonstration.</summary>
public class PilotTests
{
    [Fact]
    public void The_computer_pilot_clears_the_first_round_and_docks()
    {
        var g = Harness.Game(seed: 3);
        g.Go();
        bool docked = false;
        for (int t = 0; t < 60 * 150 && g.World.Round == 0 && g.State == GameState.Playing; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Docked && g.World.Mother is { Docked: true }) docked = true;
        }
        Assert.Equal(1, g.World.Round);
        Assert.True(docked);
        Assert.True(g.Score > 8000, $"score {g.Score}");
        Assert.True(g.World.Ships.Count >= Field.StartShips, $"{g.World.Ships.Count} ships left");
    }

    [Fact]
    public void In_the_tunnel_it_gets_round_a_barrier_coming_at_it()
    {
        var g = Harness.Game();
        g.GoToTunnel();
        var w = g.World;
        while (w.Ships.Count < Field.MaxShips) Sim.AddShips(w, 1, quiet: true);
        w.SpawnClock = w.BarrierClock = 1e9;
        // Right across the middle, where the squadron is.
        w.Incoming.Add(new Incoming { Kind = Threat.Barrier, Pos = -0.4, To = 0.25, Z = 6, Vz = -3 });
        for (int t = 0; t < 60 * 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(Field.MaxShips, w.Ships.Count);
    }

    [Fact]
    public void It_keeps_a_squadron_alive_for_a_good_while_and_scores()
    {
        var g = Harness.Game(seed: 2);
        g.Go(2);
        int t = 0;
        for (; t < 60 * 180 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.World.Round >= 4, $"round {g.World.Round + 1}");
        Assert.True(g.Score > 30000, $"score {g.Score}");
    }
}
