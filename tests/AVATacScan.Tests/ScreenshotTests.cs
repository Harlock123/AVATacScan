using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVATacScan.Engine;
using AVATacScan.Views;

namespace AVATacScan.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set TACSCAN_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Harness.TempFolder()), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Captures the screen; only the README's pictures (<paramref name="keep"/>) are saved.</summary>
    private static void Shot(Window w, GameCanvas c, string name, bool keep = true)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("TACSCAN_SHOTS");
        if (string.IsNullOrEmpty(dir) || !keep) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVATacScan-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            // A full squadron for the pictures: they're of the game, not of the pilot's luck.
            while (g.World.Ships.Count < Field.MaxShips) Sim.AddShips(g.World, 1, quiet: true);
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Play(string name, Action<GameEngine> start, Func<GameEngine, bool> moment, int seconds = 120, bool keep = true)
    {
        var (w, c, g) = Open();
        start(g);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name, keep);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        for (int i = 0; i < 60 * 3; i++) c.Advance(1 / 60.0);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 312000, 14);
        g.HighScores.Insert("AVA", 158000, 8);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        c.Advance(1);
        Shot(w, c, "high-scores", keep: false);
    }

    [AvaloniaFact]
    public void A_flat_wave() => Play("flat-wave", g => g.Go(2), g => g.World.Enemies.Count(e => e.Y is > 80 and < 450) >= 5
        && g.World.Bullets.Count >= 2 && g.World.Shots.Count >= 7 && g.World.Blasts.Count >= 1);

    [AvaloniaFact]
    public void The_tunnel() => Play("tunnel", g => g.GoToTunnel(2), g => g.World.Flown > 6 && g.World.Incoming.Count(e => e.Kind != Threat.Barrier) >= 4
        && g.World.Incoming.Any(e => e.Kind == Threat.Barrier && e.Z is > 2.5 and < 6) && g.World.Shots.Count >= 7);

    [AvaloniaFact]
    public void The_warp() => Play("warp", g => { g.Go(0); g.World.Killed = g.World.Quota - 1; g.World.Wave = 1; },
        g => g.Phase == Phase.Warp && g.PhaseTime > 1.2, keep: false);

    [AvaloniaFact]
    public void The_mother_ship() => Play("mother-ship", g => g.GoToDock(1), g => g.World.Mother is { Arrived: false, Z: < 2.4 });
}
