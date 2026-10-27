using AVATacScan.Engine;
using static AVATacScan.Tests.Harness;

namespace AVATacScan.Tests;

/// <summary>The squadron, its guns and the flat waves.</summary>
public class GameplayTests
{
    // ───────────────────────────── the squadron ─────────────────────────────

    [Fact]
    public void A_new_game_starts_with_four_ships_in_an_arrowhead_the_leader_in_front()
    {
        var g = Game();
        g.StartGame();
        var w = g.World;
        Assert.Equal(Field.StartShips, w.Ships.Count);
        g.Run(120);
        var lead = At(w, 0);
        for (int i = 1; i < w.Ships.Count; i++)
        {
            var (x, y) = At(w, i);
            Assert.True(y > lead.Y + 5, $"ship {i} is behind the leader (lower on the screen)");
            Assert.Equal(Field.Slots[i].Side < 0, x < lead.X);
        }
    }

    [Fact]
    public void The_whole_squadron_moves_together_along_the_track()
    {
        var w = Flat(5);
        var before = Enumerable.Range(0, 5).Select(i => At(w, i)).ToArray();
        Run(w, 20, new Controls(Spin: 1));
        Assert.Equal(20 * Field.SpinSpeed * Field.Step, w.Lead, 6);
        for (int i = 0; i < 5; i++) Assert.True(At(w, i).X > before[i].X + 20, $"ship {i} went right");
        // They keep their spacing.
        Assert.Equal(double.Hypot(before[2].X - before[0].X, before[2].Y - before[0].Y), double.Hypot(At(w, 2).X - At(w, 0).X, At(w, 2).Y - At(w, 0).Y), 6);
    }

    [Fact]
    public void The_squadron_can_go_to_the_ends_of_the_track_but_no_further()
    {
        foreach (int n in new[] { 1, 4, 7 })
        {
            var w = Flat(n);
            Run(w, 200, new Controls(Spin: 1));
            double right = w.Ships.Max(s => Sim.PosOf(w, s));
            Assert.Equal(1, right, 6);
            Run(w, 400, new Controls(Spin: -1));
            double left = w.Ships.Min(s => Sim.PosOf(w, s));
            Assert.Equal(-1, left, 6);
            Assert.All(w.Ships, s => Assert.InRange(At(w, w.Ships.IndexOf(s)).X, 0, Field.Width));
        }
    }

    [Fact]
    public void A_wider_squadron_has_less_room_to_move()
    {
        var (min1, max1) = Field.LeadRange(1);
        var (min7, max7) = Field.LeadRange(7);
        Assert.True(max7 - min7 < max1 - min1);
    }

    [Fact]
    public void Steering_flies_the_leader_to_a_place_on_the_track()
    {
        var w = Flat();
        Run(w, 90, new Controls(Steer: true, Target: -0.55));
        Assert.Equal(-0.55, w.Lead, 6);
    }

    [Fact]
    public void Steering_is_no_faster_than_a_hard_turn_of_the_spinner()
    {
        var w = Flat();
        Run(w, 10, new Controls(Steer: true, Target: 0.8));
        Assert.Equal(10 * Field.SteerSpeed * Field.Step, w.Lead, 6);
    }

    // ───────────────────────────── the guns ─────────────────────────────

    [Fact]
    public void Every_ship_fires_at_once_and_the_shots_fly_up_the_screen()
    {
        var w = Flat(6);
        Sim.StepFlat(w, new Controls(Fire: true), spawn: false);
        Assert.Equal(6, w.Shots.Count);
        Assert.Single(w.Heard, Sfx.Fire);
        var ys = w.Shots.Select(s => s.Y).ToArray();
        Run(w, 10);
        for (int i = 0; i < 6; i++) Assert.True(w.Shots[i].Y < ys[i] - 100);
        Run(w, 60);
        Assert.Empty(w.Shots);
    }

    [Fact]
    public void Holding_fire_fires_volley_after_volley_but_no_faster_than_the_guns_reload()
    {
        var w = Flat(1);
        int volleys = 0;
        for (int i = 0; i < 60; i++)
        {
            Sim.StepFlat(w, new Controls(Fire: true), spawn: false);
            volleys += w.Heard.Count(s => s == Sfx.Fire);
            w.Heard.Clear();
        }
        Assert.InRange(volleys, 4, (int)(1 / Field.Reload) + 1);
    }

    [Fact]
    public void A_shot_destroys_an_enemy_in_its_path_for_points()
    {
        var w = Flat(1);
        var (x, y) = At(w, 0);
        var e = Still(w, Foe.Dart, x, y - 300);
        Sim.StepFlat(w, new Controls(Fire: true), spawn: false);
        Run(w, 30);
        Assert.DoesNotContain(e, w.Enemies);
        Assert.Equal(Enemy.Points(Foe.Dart), w.Score);
        Assert.Equal(1, w.Killed);
        Assert.NotEmpty(w.Blasts);
    }

    [Fact]
    public void A_shot_misses_an_enemy_off_to_the_side()
    {
        var w = Flat(1);
        var (x, y) = At(w, 0);
        var e = Still(w, Foe.Dart, x + 60, y - 300);
        Sim.StepFlat(w, new Controls(Fire: true), spawn: false);
        Run(w, 40);
        Assert.Contains(e, w.Enemies);
    }

    [Fact]
    public void A_spinner_takes_two_hits()
    {
        var w = Flat(1);
        var (x, y) = At(w, 0);
        var e = Still(w, Foe.Spinner, x, y - 250);
        Sim.StepFlat(w, new Controls(Fire: true), spawn: false);
        Run(w, 30);
        Assert.Contains(e, w.Enemies);
        Assert.Equal(1, e.Hp);
        Sim.StepFlat(w, new Controls(Fire: true), spawn: false);
        Run(w, 30);
        Assert.DoesNotContain(e, w.Enemies);
        Assert.Equal(Enemy.Points(Foe.Spinner), w.Score);
    }

    // ───────────────────────────── losing ships ─────────────────────────────

    [Fact]
    public void Enemy_fire_takes_the_ship_it_hits_and_the_squadron_closes_up()
    {
        var w = Flat(5);
        var (x, y) = At(w, 1);
        var hit = w.Ships[1];
        w.Bullets.Add(new Bullet { X = x, Y = y - 40, Vy = 300 });
        Run(w, 20);
        Assert.Equal(4, w.Ships.Count);
        Assert.DoesNotContain(hit, w.Ships);
        Assert.Equal(1, w.Lost);
        // The ships behind slide up into the empty slots.
        Run(w, 90);
        for (int i = 0; i < w.Ships.Count; i++)
        {
            Assert.Equal(Field.Slots[i].Side, w.Ships[i].Side, 6);
            Assert.Equal(Field.Slots[i].Row, w.Ships[i].Row, 6);
        }
    }

    [Fact]
    public void Fire_that_misses_the_ships_does_no_harm()
    {
        var w = Flat(1);
        var (x, y) = At(w, 0);
        w.Bullets.Add(new Bullet { X = x + 40, Y = y - 40, Vy = 300 });
        Run(w, 40);
        Assert.Single(w.Ships);
        Assert.Empty(w.Bullets);
    }

    [Fact]
    public void An_enemy_flying_into_a_ship_takes_it_and_is_destroyed_too()
    {
        var w = Flat(3);
        var (x, y) = At(w, 2);
        var e = Still(w, Foe.Dart, x, y - 60);
        e.Vy = 300;
        Run(w, 20);
        Assert.Equal(2, w.Ships.Count);
        Assert.DoesNotContain(e, w.Enemies);
        Assert.Equal(1, w.Killed);
    }

    [Fact]
    public void Losing_the_last_ship_ends_the_game()
    {
        var g = Game();
        g.Go();
        var w = g.World;
        while (w.Ships.Count > 1) w.Ships.RemoveAt(w.Ships.Count - 1);
        var (x, y) = At(w, 0);
        w.Bullets.Add(new Bullet { X = x, Y = y - 30, Vy = 300 });
        g.Run(20);
        Assert.Equal(Phase.Down, g.Phase);
        Assert.Equal(GameState.Playing, g.State);
        g.Run((int)(GameEngine.DownSeconds * 60) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── extra ships ─────────────────────────────

    [Fact]
    public void Points_bring_extra_ships_but_never_more_than_seven()
    {
        var w = Flat(3);
        Sim.AddScore(w, Field.ExtraShipAt - 1);
        Assert.Equal(3, w.Ships.Count);
        Sim.AddScore(w, 1);
        Assert.Equal(4, w.Ships.Count);
        Assert.Contains(Sfx.ExtraShip, w.Heard);
        Sim.AddScore(w, Field.ExtraShipEvery);
        Assert.Equal(5, w.Ships.Count);
        Assert.Equal(1, Sim.AddShips(w, 3));
        Assert.Equal(Field.MaxShips, w.Ships.Count);
    }

    [Fact]
    public void A_new_ship_slides_up_into_its_slot_from_behind()
    {
        var w = Flat(2);
        Sim.AddShips(w, 1);
        var s = w.Ships[2];
        Assert.True(s.Row > Field.Slots[2].Row);
        Run(w, 60);
        Assert.Equal(Field.Slots[2].Row, s.Row, 6);
    }

    // ───────────────────────────── the waves ─────────────────────────────

    [Fact]
    public void A_wave_brings_its_enemies_on_a_few_at_a_time_up_to_its_quota()
    {
        var w = Flat(7);
        int most = 0;
        for (int i = 0; i < 60 * 120 && w.Spawned < w.Quota; i++)
        {
            Sim.StepFlat(w, default);
            most = Math.Max(most, w.Enemies.Count);
            // Kill them off as they come so more can.
            if (i % 120 == 0 && w.Enemies.Count > 0) { w.Enemies.RemoveAt(0); w.Killed++; }
            w.Ships.Clear();
            for (int k = 0; k < 7; k++) w.Ships.Add(new Ship { Side = Field.Slots[k].Side, Row = Field.Slots[k].Row });
            w.Bullets.Clear();
        }
        Assert.Equal(w.Quota, w.Spawned);
        // Darts come in a string, which can take it a few over.
        Assert.InRange(most, 3, Sim.Crowd(0) + 4);
    }

    [Fact]
    public void Later_rounds_bring_more_kinds_more_of_them_and_faster()
    {
        Assert.DoesNotContain(Foe.Bomber, Sim.Mix(0, 0).Concat(Sim.Mix(0, 1)));
        Assert.Contains(Foe.Bomber, Sim.Mix(1, 0));
        Assert.Equal(4, Sim.Mix(3, 0).Distinct().Count());
        Assert.True(Field.Quota(3, 0) > Field.Quota(0, 0));
        Assert.True(Field.Pace(3) > Field.Pace(0));
        Assert.True(Sim.Crowd(3) > Sim.Crowd(0));
    }

    [Fact]
    public void Darts_swing_round_the_top_then_dive_at_the_squadron()
    {
        var w = Flat(1);
        var (sx, _) = At(w, 0);
        var e = new Enemy { Kind = Foe.Dart, Hp = 1, Dir = 1, Ox = 300, Oy = 200, X = 400, Y = -30, Fire = 1e9 };
        w.Enemies.Add(e);
        int i = 0;
        for (; i < 60 * 8 && e.Mode < 2; i++) Sim.StepFlat(w, default, spawn: false);
        Assert.Equal(2, e.Mode);
        Assert.True(e.Y < 320, "it swung round near the top");
        Assert.True(e.Vy > 0 && Math.Sign(e.Vx) == Math.Sign(sx - e.X), "and dives towards the ship");
    }

    [Fact]
    public void Saucers_that_slip_past_come_round_again_from_the_top()
    {
        var w = Flat(1);
        w.Lead = -0.9;
        var e = new Enemy { Kind = Foe.Saucer, Hp = 1, Ox = 900, X = 900, Y = Field.Height + 20, Fire = 1e9 };
        w.Enemies.Add(e);
        Run(w, 30);
        Assert.Contains(e, w.Enemies);
        Assert.True(e.Y < 100);
    }

    [Fact]
    public void Bombers_bomb_only_what_is_under_them()
    {
        var w = Flat(1);
        var (sx, _) = At(w, 0);
        var b = new Enemy { Kind = Foe.Bomber, Hp = 1, Dir = 1, X = sx - 300, Y = 120, Vx = 150 };
        w.Enemies.Add(b);
        Run(w, 60);
        Assert.Empty(w.Bullets);
        Run(w, 70);
        var bomb = Assert.Single(w.Bullets);
        Assert.True(bomb.Bomb);
        Assert.InRange(bomb.X, sx - 25, sx + 25);
    }

    [Fact]
    public void Spinners_aim_their_fire_at_a_ship()
    {
        var w = Flat(1);
        var (sx, sy) = At(w, 0);
        var e = Still(w, Foe.Spinner, sx - 200, 200);
        e.Fire = 0;
        Sim.StepFlat(w, default, spawn: false);
        var b = Assert.Single(w.Bullets);
        Assert.Equal((sx - b.X) / (sy - b.Y), b.Vx / b.Vy, 2);
    }

    // ───────────────────────────── the round ─────────────────────────────

    [Fact]
    public void Clearing_a_flat_wave_brings_the_next_and_the_second_leads_into_the_tunnel()
    {
        var g = Game();
        g.Go();
        var w = g.World;
        w.Killed = w.Quota;
        g.Run(1);
        Assert.Equal(1, w.Wave);
        Assert.Equal(Phase.Ready, g.Phase);
        Assert.Equal("WAVE 2", g.Message);
        g.Run((int)(GameEngine.WaveSeconds * 60) + 2);
        Assert.Equal(Phase.Wave, g.Phase);
        Assert.Equal(0, w.Killed);
        w.Killed = w.Quota;
        g.Run(1);
        Assert.Equal(Phase.Warp, g.Phase);
        Assert.False(w.Tunnel);
        g.Run((int)(GameEngine.WarpSeconds * 60) + 2);
        Assert.Equal(Phase.Tunnel, g.Phase);
        Assert.True(w.Tunnel);
    }

    [Fact]
    public void A_round_starts_with_its_call_and_no_enemies_until_the_wave()
    {
        var g = Game();
        g.StartGame();
        Assert.Equal("ROUND 1", g.Message);
        g.Run((int)(GameEngine.RoundSeconds * 60) - 5);
        Assert.Empty(g.World.Enemies);
        Assert.Equal(Phase.Ready, g.Phase);
        g.Run(120);
        Assert.Equal(Phase.Wave, g.Phase);
        Assert.NotEmpty(g.World.Enemies);
    }
}
