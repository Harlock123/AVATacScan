using AVATacScan.Engine;
using static AVATacScan.Tests.Harness;

namespace AVATacScan.Tests;

/// <summary>The tunnel: the view into it, what comes down it, and docking with the mother ship.</summary>
public class TunnelTests
{
    // ───────────────────────────── the view ─────────────────────────────

    [Fact]
    public void Deeper_down_the_tube_is_nearer_the_vanishing_point_and_smaller()
    {
        var (x1, y1, s1) = Field.Tube(0.3, 1);
        var (x2, y2, s2) = Field.Tube(0.3, 2);
        var (x9, y9, _) = Field.Tube(0.3, 200);
        Assert.Equal(s1 / 2, s2, 6);
        Assert.Equal((x1 - Field.Cx) / 2, x2 - Field.Cx, 6);
        Assert.Equal((y1 - Field.Cy) / 2, y2 - Field.Cy, 6);
        Assert.InRange(x9, Field.Cx - 2, Field.Cx + 2);
        Assert.InRange(y9, Field.Cy - 2, Field.Cy + 2);
    }

    [Fact]
    public void The_track_runs_round_the_bottom_of_the_tube_left_to_right()
    {
        var (xm, ym, _) = Field.Tube(0, Field.ShipZ);
        var (xl, yl, _) = Field.Tube(-1, Field.ShipZ);
        var (xr, yr, _) = Field.Tube(1, Field.ShipZ);
        Assert.Equal(Field.Cx, xm, 6);
        Assert.True(ym > Field.Cy + 200, "the middle of the track is at the bottom");
        Assert.True(xl < Field.Cx - 200 && xr > Field.Cx + 200);
        Assert.True(yl > Field.Cy && yr > Field.Cy, "the ends are still below the vanishing point");
        Assert.Equal(yl, yr, 6);
    }

    [Fact]
    public void The_flat_track_is_a_shallow_arc_at_the_foot_of_the_screen_its_ships_facing_up()
    {
        var (xm, ym, dx, dy) = Field.Flat(0);
        var (xl, yl, ldx, _) = Field.Flat(-1);
        var (xr, yr, rdx, _) = Field.Flat(1);
        Assert.Equal(Field.Cx, xm, 6);
        Assert.InRange(ym, Field.Height - 120, Field.Height - 40);
        Assert.True(xl < 150 && xr > Field.Width - 150);
        Assert.True(yl < ym && yr < ym, "the ends curl up");
        Assert.Equal((0, -1), (Math.Round(dx, 6), Math.Round(dy, 6)));
        Assert.True(ldx > 0 && rdx < 0, "the ships at the ends face in a little");
    }

    [Fact]
    public void A_point_on_the_screen_maps_back_to_the_place_on_the_track_drawn_there()
    {
        foreach (double p in new[] { -0.9, -0.3, 0, 0.45, 1 })
        {
            var (x, y, _, _) = Field.Flat(p);
            Assert.Equal(p, Field.FlatPosAt(x, y), 6);
            var (tx, ty, _) = Field.Tube(p, Field.ShipZ);
            Assert.Equal(p, Field.TunnelPosAt(tx, ty), 6);
        }
    }

    // ───────────────────────────── shooting down the tube ─────────────────────────────

    [Fact]
    public void Shots_fly_down_the_tube_and_hit_what_is_lined_up_with_them()
    {
        var w = Tunnel(1);
        var e = Coming(w, Threat.Fighter, 0.02, 6, vz: 0);
        Sim.StepTunnel(w, new Controls(Fire: true), spawn: false);
        var shot = Assert.Single(w.Shots);
        Assert.Equal(0, shot.Pos, 6);
        Run(w, 30);
        Assert.True(e.Dead);
        Assert.Empty(w.Incoming);
        Assert.Equal(Incoming.Points(Threat.Fighter), w.Score);
    }

    [Fact]
    public void Shots_pass_by_what_is_off_to_the_side()
    {
        var w = Tunnel(1);
        var e = Coming(w, Threat.Fighter, 0.25, 6, vz: 0);
        Sim.StepTunnel(w, new Controls(Fire: true), spawn: false);
        Run(w, 50);
        Assert.False(e.Dead);
        Assert.Empty(w.Shots);
    }

    [Fact]
    public void A_barrier_cannot_be_shot_it_soaks_up_the_shots()
    {
        var w = Tunnel(3);
        var e = Coming(w, Threat.Barrier, -0.3, 5, vz: 0, to: 0.3);
        Sim.StepTunnel(w, new Controls(Fire: true), spawn: false);
        Run(w, 40);
        Assert.False(e.Dead);
        Assert.Empty(w.Shots);
        Assert.Equal(0, w.Score);
    }

    // ───────────────────────────── what comes at the squadron ─────────────────────────────

    [Fact]
    public void A_fighter_reaching_the_squadron_takes_only_the_ship_under_it()
    {
        var w = Tunnel(3);
        var hit = w.Ships[2];
        var e = Coming(w, Threat.Fighter, Sim.PosOf(w, hit), 3);
        Run(w, 60);
        Assert.Equal(2, w.Ships.Count);
        Assert.DoesNotContain(hit, w.Ships);
        Assert.True(e.Dead);
    }

    [Fact]
    public void A_fighter_beside_the_squadron_does_no_harm()
    {
        var w = Tunnel(3);
        Coming(w, Threat.Fighter, 0.3, 3);
        Run(w, 90);
        Assert.Equal(3, w.Ships.Count);
        Assert.Empty(w.Incoming);
    }

    [Fact]
    public void A_barrier_takes_every_ship_under_it_and_none_beside_it()
    {
        var w = Tunnel(7);
        // Across the leader and the ships to its right.
        Coming(w, Threat.Barrier, -0.02, 3, to: 0.4);
        Run(w, 90);
        Assert.Equal(3, w.Ships.Count);
        Assert.Equal(4, w.Lost);
    }

    [Fact]
    public void A_barrier_always_leaves_room_for_a_full_squadron_to_get_by()
    {
        var w = Tunnel(7);
        var (l, r) = Field.Spread(7);
        double width = (r - l) * Field.SlotSpacing + 0.08;
        for (int i = 0; i < 400; i++)
        {
            w.Incoming.Clear();
            w.BarrierClock = 0;
            w.SpawnClock = 1e9;
            w.Round = i % 8;
            Sim.StepTunnel(w, default);
            var b = Assert.Single(w.Incoming, e => e.Kind == Threat.Barrier);
            double gap = Math.Max(b.Pos - -1, 1 - b.To);
            Assert.True(gap >= width, $"gap {gap:0.00} for a squadron {width:0.00} wide");
        }
    }

    [Fact]
    public void Seekers_turn_round_the_tube_towards_the_squadron()
    {
        var w = Tunnel(1, round: 2);
        w.Lead = 0.6;
        var e = Coming(w, Threat.Seeker, -0.6, 9, vz: -1);
        e.Vpos = 0.3;
        Run(w, 120);
        Assert.Equal(0, e.Pos, 1);
    }

    [Fact]
    public void Fighters_fire_bolts_down_the_tube_which_take_a_ship()
    {
        var w = Tunnel(1);
        var e = Coming(w, Threat.Fighter, 0.6, 7, vz: -2);
        e.Fire = 0;
        Sim.StepTunnel(w, default, spawn: false);
        var b = Assert.Single(w.Bolts);
        Assert.Equal(0.6, b.Pos, 6);
        Assert.True(b.Vz < e.Vz, "faster than the fighter");
        w.Lead = 0.6;
        Run(w, 100);
        Assert.Empty(w.Ships);
        Assert.False(e.Dead, "the bolt got there first");
    }

    [Fact]
    public void The_tunnel_fills_with_fighters_and_barriers_as_it_goes()
    {
        var w = Tunnel(7, round: 2);
        int fighters = 0, barriers = 0, seekers = 0;
        for (int i = 0; i < 60 * 20; i++)
        {
            int before = w.Incoming.Count;
            Sim.StepTunnel(w, default);
            foreach (var e in w.Incoming.Skip(Math.Max(0, before - 0)).Where(e => e.T == Field.Step))
            {
                if (e.Kind == Threat.Fighter) fighters++;
                else if (e.Kind == Threat.Barrier) barriers++;
                else seekers++;
            }
            // Nothing is allowed to reach them: this is about what comes.
            w.Ships.Clear();
            for (int k = 0; k < 7; k++) w.Ships.Add(new Ship { Side = Field.Slots[k].Side, Row = Field.Slots[k].Row });
        }
        Assert.True(fighters > 15, $"{fighters} fighters");
        Assert.True(seekers > 2, $"{seekers} seekers");
        Assert.InRange(barriers, 2, 8);
    }

    // ───────────────────────────── the mother ship ─────────────────────────────

    [Fact]
    public void At_the_end_of_the_tunnel_the_mother_ship_comes()
    {
        var g = Game();
        g.GoToTunnel();
        g.World.Flown = Field.TunnelSeconds(0) - 0.01;
        g.Run(2);
        Assert.Equal(Phase.Dock, g.Phase);
        Assert.NotNull(g.World.Mother);
        Assert.Equal("THE MOTHER SHIP", g.Message);
    }

    [Fact]
    public void Lined_up_under_the_bay_the_squadron_docks_for_two_ships_and_a_bonus()
    {
        var g = Game();
        g.GoToDock();
        g.Run(1);
        var m = g.World.Mother!;
        int ships = g.World.Ships.Count, score = g.Score;
        for (int i = 0; i < 60 * 10 && g.Phase == Phase.Dock; i++) g.Tick(new Controls(Steer: true, Target: m.Bay));
        Assert.Equal(Phase.Docked, g.Phase);
        Assert.True(m.Docked);
        Assert.Equal(ships + Field.DockShips, g.World.Ships.Count);
        Assert.Equal(score + GameEngine.DockBonus(0), g.Score);
        Assert.Equal("DOCKED!", g.Message);
    }

    [Fact]
    public void Out_of_line_the_docking_is_missed_and_brings_nothing()
    {
        var g = Game();
        g.GoToDock();
        g.Run(1);
        var m = g.World.Mother!;
        int ships = g.World.Ships.Count, score = g.Score;
        double away = m.Bay > 0 ? m.Bay - 0.3 : m.Bay + 0.3;
        for (int i = 0; i < 60 * 10 && g.Phase == Phase.Dock; i++) g.Tick(new Controls(Steer: true, Target: away));
        Assert.Equal(Phase.Docked, g.Phase);
        Assert.False(m.Docked);
        Assert.Equal(ships, g.World.Ships.Count);
        Assert.Equal(score, g.Score);
        Assert.Equal("DOCKING MISSED", g.Message);
    }

    [Fact]
    public void A_full_squadron_docking_gets_points_for_the_ships_that_do_not_fit()
    {
        var g = Game();
        g.GoToDock(2);
        Sim.AddShips(g.World, 2, quiet: true);
        g.Run(1);
        var m = g.World.Mother!;
        for (int i = 0; i < 60 * 10 && g.Phase == Phase.Dock; i++) g.Tick(new Controls(Steer: true, Target: m.Bay));
        Assert.Equal(Field.MaxShips, g.World.Ships.Count);
        Assert.Equal(GameEngine.DockBonus(2) + Field.SpareShipPoints, g.LastBonus);
    }

    [Fact]
    public void The_bay_is_always_somewhere_the_squadron_can_reach_and_never_right_where_it_is()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var w = Tunnel(seed % 7 + 1);
            w.Rng = new Random(seed);
            w.Lead = (seed % 5 - 2) * 0.2;
            var (min, max) = Field.LeadRange(w.Ships.Count);
            w.Lead = Math.Clamp(w.Lead, min, max);
            var m = Sim.CallMother(w);
            Assert.InRange(m.Bay, min, max);
            Assert.True(Math.Abs(m.Bay - w.Lead) > Field.DockReach);
        }
    }

    [Fact]
    public void After_docking_the_next_round_starts_back_on_the_flat()
    {
        var g = Game();
        g.GoToDock();
        g.Run(1);
        for (int i = 0; i < 60 * 20 && g.World.Round == 0; i++) g.Tick(new Controls(Steer: true, Target: g.World.Mother?.Bay ?? 0));
        Assert.Equal(1, g.World.Round);
        Assert.False(g.World.Tunnel);
        Assert.Equal(Phase.Ready, g.Phase);
        Assert.Equal("ROUND 2", g.Message);
    }
}
