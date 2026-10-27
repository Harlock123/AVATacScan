using AVATacScan.Engine;

namespace AVATacScan.Tests;

/// <summary>
/// How the computer pilot gets on (set TACSCAN_PROBE to run it, for tuning: "1" to start from the first round, or a
/// round number to start there). TACSCAN_SEEDS picks the games.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        string? mode = Environment.GetEnvironmentVariable("TACSCAN_PROBE");
        if (mode is null) return;
        int from = int.TryParse(mode, out int n) && n > 1 ? n - 1 : 0;
        var seeds = (Environment.GetEnvironmentVariable("TACSCAN_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "tacscan-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(from);
            g.Pilot.Reset();
            var losses = new List<string>();
            int t = 0, docks = 0, misses = 0, ships = g.World.Ships.Count;
            var phase = g.Phase;
            for (; t < 60 * 900 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                var w = g.World;
                if (w.Ships.Count < ships) losses.Add($"{w.Round + 1}:{g.Phase}");
                ships = w.Ships.Count;
                if (g.Phase == Phase.Docked && phase != Phase.Docked) { if (w.Mother!.Docked) docks++; else misses++; }
                phase = g.Phase;
            }
            output.WriteLine($"seed {seed}: {g.State}/{g.Phase} at round {g.World.Round + 1} after {t / 60}s, score {g.Score}, ships {g.World.Ships.Count}, docked {docks}, missed {misses}, lost [{string.Join(" ", losses)}]");
        }
    }
}
