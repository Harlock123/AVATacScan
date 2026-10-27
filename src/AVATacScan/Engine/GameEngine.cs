namespace AVATacScan.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A round's (or a wave's) start: its call, the squadron flying in, no enemies yet.</summary>
    Ready,
    /// <summary>A flat wave: shoot down its quota.</summary>
    Wave,
    /// <summary>The jump from the flat into the tunnel.</summary>
    Warp,
    /// <summary>Down the tunnel, everything coming out of the vanishing point.</summary>
    Tunnel,
    /// <summary>The mother ship coming down the tunnel to dock the squadron.</summary>
    Dock,
    /// <summary>Docked (or not): the bonus, before the next round.</summary>
    Docked,
    /// <summary>The last ship gone, before the game ends.</summary>
    Down,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, Blast, ShipLost, EnemyFire, Bomb, Clang, Warp, MotherShip, Docked, DockFailed, ExtraShip,
    RoundTune, WaveClear, GameOver, MusicStop,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 14;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double RoundSeconds = 3, WaveSeconds = 1.8, WarpSeconds = 2.4, DockedSeconds = 3.6, DownSeconds = 2.6;

    private double _accumulator, _idle;
    private bool _startPrev;
    private readonly int _seed;
    private int _games;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Ready;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Score => World.Score;
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The points the last docking was worth, and the ships it brought.</summary>
    public int LastBonus { get; private set; }
    public int LastShips { get; private set; }

    /// <summary>Points for docking on a round (0 is the first).</summary>
    public static int DockBonus(int round) => 1000 * (round + 1);

    /// <summary>How far through the warp the squadron is, 0 to 1 (1 outside the warp once in the tunnel).</summary>
    public double WarpFraction => Phase == Phase.Warp ? Math.Clamp(PhaseTime / WarpSeconds, 0, 1) : World.Tunnel ? 1 : 0;

    private void Play(Sfx s)
    {
        // The demonstration plays in silence, the way the arcade machines' attract modes mostly did.
        if (!Silent && State != GameState.Attract) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, World.Round + 1);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Sim.Start(World, _seed * 1000 + _games++);
        SetPhase(Phase.Ready);
        Message = SubMessage = null;
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.MusicStop);
        Play(Sfx.RoundTune);
        Callout();
    }

    private void StartAttract()
    {
        Play(Sfx.MusicStop);
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
        Callout();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        Play(Sfx.MusicStop);
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        Play(Sfx.MusicStop);
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    /// <summary>A round called out at its start.</summary>
    private void Callout() => Say($"ROUND {World.Round + 1}", "GET READY", RoundSeconds);

    private double ReadyLength => World.Wave == 0 ? RoundSeconds : WaveSeconds;

    private void Simulate(Controls c)
    {
        double dt = Field.Step;
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Ready:
                Sim.StepFlat(w, c, spawn: false);
                if (w.Wiped) { Down(); break; }
                if (PhaseTime > ReadyLength) SetPhase(Phase.Wave);
                break;
            case Phase.Wave:
                Sim.StepFlat(w, c);
                if (w.Wiped) Down();
                else if (w.WaveDone) WaveClear();
                break;
            case Phase.Warp:
                Sim.StepWarp(w, c);
                if (PhaseTime > WarpSeconds)
                {
                    Sim.EnterTunnel(w);
                    SetPhase(Phase.Tunnel);
                }
                break;
            case Phase.Tunnel:
                Sim.StepTunnel(w, c, spawn: w.Flown < Field.TunnelSeconds(w.Round) - 2.5);
                if (w.Wiped) Down();
                else if (w.Flown >= Field.TunnelSeconds(w.Round))
                {
                    Sim.CallMother(w);
                    SetPhase(Phase.Dock);
                    Say("THE MOTHER SHIP", "LINE YOUR LEADER UP UNDER ITS BAY TO DOCK", 3.5);
                }
                break;
            case Phase.Dock:
                Sim.StepTunnel(w, c, spawn: false);
                if (w.Wiped) Down();
                else if (w.Mother is { Arrived: true } m) Docked(m);
                break;
            case Phase.Docked:
                Sim.StepTunnel(w, c, spawn: false);
                if (PhaseTime > DockedSeconds) NextRound();
                break;
            case Phase.Down:
                if (w.Tunnel) Sim.StepTunnel(w, default, spawn: false);
                else Sim.StepFlat(w, default, spawn: false);
                if (PhaseTime > DownSeconds) GameOver();
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void Down()
    {
        SetPhase(Phase.Down);
        Say("SQUADRON LOST", null, DownSeconds);
    }

    private void WaveClear()
    {
        var w = World;
        w.Wave++;
        Play(Sfx.WaveClear);
        if (w.Wave < Field.WavesPerRound)
        {
            Sim.NextWave(w);
            SetPhase(Phase.Ready);
            Say($"WAVE {w.Wave + 1}", null, WaveSeconds);
            return;
        }
        // The flat's last shots and fire are left behind.
        w.Enemies.Clear();
        w.Bullets.Clear();
        w.Shots.Clear();
        SetPhase(Phase.Warp);
        Play(Sfx.Warp);
        Say("INTO THE TUNNEL", null, WarpSeconds);
    }

    private void Docked(MotherShip m)
    {
        var w = World;
        SetPhase(Phase.Docked);
        if (m.Docked)
        {
            int spare = Sim.AddShips(w, Field.DockShips, quiet: true);
            LastShips = Field.DockShips - spare;
            LastBonus = DockBonus(w.Round) + spare * Field.SpareShipPoints;
            Sim.AddScore(w, LastBonus);
            Say("DOCKED!", LastShips > 0 ? $"+{LastShips} SHIP{(LastShips > 1 ? "S" : "")}   BONUS {LastBonus}" : $"SQUADRON FULL   BONUS {LastBonus}", DockedSeconds);
            Play(Sfx.Docked);
        }
        else
        {
            LastShips = LastBonus = 0;
            Say("DOCKING MISSED", "NO NEW SHIPS THIS ROUND", DockedSeconds);
            Play(Sfx.DockFailed);
        }
    }

    private void NextRound()
    {
        var w = World;
        Sim.Begin(w, w.Round + 1);
        SetPhase(Phase.Ready);
        Callout();
        Play(Sfx.RoundTune);
    }

    private void GameOver()
    {
        Play(Sfx.MusicStop);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a round's first wave, past its callout.</summary>
    public void Go(int round = 0)
    {
        NewGame();
        State = GameState.Playing;
        if (round > 0) Sim.Begin(World, round);
        SetPhase(Phase.Wave);
        Message = null;
    }

    /// <summary>Straight down a round's tunnel.</summary>
    public void GoToTunnel(int round = 0)
    {
        Go(round);
        World.Wave = Field.WavesPerRound;
        Sim.EnterTunnel(World);
        SetPhase(Phase.Tunnel);
    }

    /// <summary>Straight to the end of a round's tunnel, the mother ship coming.</summary>
    public void GoToDock(int round = 0)
    {
        GoToTunnel(round);
        World.Flown = Field.TunnelSeconds(round);
        World.Incoming.Clear();
    }
}
