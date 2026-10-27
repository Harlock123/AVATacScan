using AVATacScan.Engine;
using static AVATacScan.Tests.Harness;

namespace AVATacScan.Tests;

/// <summary>The machine around the game: the title, the demonstration, the high scores, pause, and the sounds.</summary>
public class EngineTests
{
    [Fact]
    public void Left_alone_the_title_gives_way_to_the_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Game();
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 5) && g.State == GameState.Attract; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_demonstration_plays_itself_in_silence_and_enter_starts_a_real_game()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()));
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        heard.Clear();
        g.Idle(60 * 10);
        Assert.Empty(heard);
        Assert.True(g.World.Shots.Count > 0 || g.World.Killed > 0, "the pilot is playing");
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Contains(Sfx.RoundTune, heard);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void Fire_or_enter_starts_from_the_title()
    {
        var g = Game();
        g.Idle(60);
        g.Tick(new Controls(Fire: true));
        Assert.Equal(GameState.Playing, g.State);
        var h = Game();
        h.Tick(default, start: true);
        Assert.Equal(GameState.Playing, h.State);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Game();
        g.Go();
        g.Run(60);
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        double lead = g.World.Lead, clock = g.World.Clock;
        g.Run(120, new Controls(Spin: 1));
        Assert.Equal(lead, g.World.Lead);
        Assert.Equal(clock, g.World.Clock);
        g.Resume();
        g.Run(10, new Controls(Spin: 1));
        Assert.True(g.World.Lead > lead);
    }

    [Fact]
    public void A_good_score_gets_initials_on_the_table_which_is_kept_for_next_time()
    {
        string folder = TempFolder();
        var g = new GameEngine(HighScoreTable.Load(folder)) { Silent = true };
        g.Go(3);
        g.World.Score = 123450;
        g.World.Ships.Clear();
        g.Run(1);
        g.Run((int)(GameEngine.DownSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "LON") g.Initials!.Type(ch);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("LON", 123450, 4), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
    }

    [Fact]
    public void A_poor_score_goes_straight_to_the_table()
    {
        var g = Game();
        g.Go();
        g.World.Ships.Clear();
        g.Run(1);
        g.Run((int)(GameEngine.DownSeconds * 60) + 60 * 5);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(-1, g.NewEntryIndex);
    }

    [Fact]
    public void A_fresh_table_has_scores_worth_beating()
    {
        var t = HighScoreTable.Load(TempFolder());
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        Assert.InRange(t.TopScore, 20000, 200000);
    }

    [Fact]
    public void The_game_gets_harder_round_by_round()
    {
        Assert.True(Field.Pace(5) > Field.Pace(1));
        Assert.True(Field.TunnelSeconds(5) > Field.TunnelSeconds(0));
        Assert.True(Sim.Closing(new World { Round = 5 }) > Sim.Closing(new World()));
    }

    [Fact]
    public void Every_sound_has_its_file()
    {
        var sound = new SoundManager(silent: true);
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(sound.File(s)), $"{s}");
    }
}
