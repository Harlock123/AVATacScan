using Avalonia.Input;
using AVATacScan.Engine;
using static AVATacScan.Tests.Harness;

namespace AVATacScan.Tests;

/// <summary>The keys, the mouse and its wheel, through the input handler and into the game.</summary>
public sealed class InputTests
{
    /// <summary>Holds keys down through the handler for a while, and says how far across the screen the leader went
    /// (on the flat, or down the tunnel) and how many volleys were fired.</summary>
    private static (double Dx, int Volleys) Hold(InputHandler input, bool tunnel, params Key[] keys)
    {
        var w = tunnel ? Tunnel() : Flat();
        foreach (var k in keys) input.OnKeyDown(k);
        double x0 = LeaderX(w);
        int volleys = 0;
        for (int i = 0; i < 30; i++)
        {
            Run(w, 1, input.Player);
            volleys += w.Heard.Count(s => s == Sfx.Fire);
            w.Heard.Clear();
        }
        foreach (var k in keys) input.OnKeyUp(k);
        return (LeaderX(w) - x0, volleys);
    }

    private static double LeaderX(World w) => w.Tunnel ? Field.Tube(w.Lead, Field.ShipZ).X : At(w, 0).X;

    [Fact]
    public void Right_flies_the_squadron_right_on_the_screen_and_left_left_on_the_flat_and_in_the_tunnel()
    {
        using var input = new InputHandler();
        foreach (bool tunnel in new[] { false, true })
        {
            foreach (var key in new[] { Key.Right, Key.D, Key.NumPad6 }) Assert.True(Hold(input, tunnel, key).Dx > 20, $"{key} tunnel={tunnel}");
            foreach (var key in new[] { Key.Left, Key.A, Key.NumPad4 }) Assert.True(Hold(input, tunnel, key).Dx < -20, $"{key} tunnel={tunnel}");
            Assert.Equal(0, Hold(input, tunnel).Dx);
            Assert.Equal(0, Hold(input, tunnel, Key.Left, Key.Right).Dx);
        }
    }

    [Fact]
    public void Up_and_down_do_nothing_the_squadron_keeps_to_its_track()
    {
        using var input = new InputHandler();
        Assert.Equal(0, Hold(input, false, Key.Up).Dx);
        Assert.Equal(0, Hold(input, false, Key.Down, Key.W, Key.S).Dx);
    }

    [Fact]
    public void Z_Space_Ctrl_and_Shift_fire_and_holding_one_keeps_firing()
    {
        using var input = new InputHandler();
        foreach (var key in new[] { Key.Z, Key.Space, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift })
            Assert.True(Hold(input, false, key).Volleys >= 2, $"{key}");
        Assert.Equal(0, Hold(input, false, Key.X).Volleys);
    }

    [Fact]
    public void Moving_the_mouse_flies_the_squadron_to_the_pointer_and_a_click_fires()
    {
        using var input = new InputHandler();
        var w = Flat();
        var (sx, sy, _, _) = Field.Flat(-0.5);
        input.MouseAt(sx, sy);
        input.MouseButton = true;
        Assert.True(input.Player.Steer);
        Assert.True(input.Player.Fire);
        Run(w, 90, input.Player);
        Assert.Equal(-0.5, w.Lead, 3);
        Assert.NotEmpty(w.Shots);
    }

    [Fact]
    public void In_the_tunnel_the_mouse_steers_round_the_rim()
    {
        using var input = new InputHandler { TrackAt = Field.TunnelPosAt };
        var w = Tunnel();
        var (sx, sy, _) = Field.Tube(0.6, Field.ShipZ);
        input.MouseAt(sx, sy);
        Run(w, 90, input.Player);
        Assert.Equal(0.6, w.Lead, 3);
        // Above the vanishing point it goes by how far across the pointer is.
        input.MouseAt(Field.Cx - 400, 100);
        Run(w, 120, input.Player);
        Assert.Equal(Field.LeadRange(w.Ships.Count).Min, w.Lead, 3);
    }

    [Fact]
    public void A_key_takes_over_from_the_mouse_until_the_mouse_moves_again()
    {
        using var input = new InputHandler();
        input.MouseAt(300, 600);
        Assert.True(input.Player.Steer);
        input.OnKeyDown(Key.Right);
        Assert.False(input.Player.Steer);
        Assert.Equal(1, input.Player.Spin);
        input.OnKeyUp(Key.Right);
        Assert.False(input.Player.Steer, "the squadron stays where the key left it");
        input.MouseAt(300, 600);
        Assert.False(input.Player.Steer, "the pointer didn't move");
        input.MouseAt(320, 600);
        Assert.True(input.Player.Steer);
    }

    [Fact]
    public void The_mouse_wheel_turns_the_squadron_a_notch_at_a_time_like_the_spinner()
    {
        using var input = new InputHandler();
        var w = Flat();
        input.Wheel(1);
        for (int i = 0; i < 30; i++)
        {
            Run(w, 1, input.Player);
            input.Update();
        }
        Assert.True(w.Lead > 0.05);
        double was = w.Lead;
        input.Wheel(-1);
        input.Wheel(-1);
        for (int i = 0; i < 30; i++)
        {
            Run(w, 1, input.Player);
            input.Update();
        }
        Assert.True(w.Lead < was - 0.1);
    }

    [Fact]
    public void Enter_starts()
    {
        using var input = new InputHandler();
        Assert.False(input.Start);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
    }
}
