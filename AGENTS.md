# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A colour vector shooter in the style of Sega's 1982 arcade game Tac/Scan, in C# on .NET 10 and Avalonia 12. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The vector graphics are drawn in
code, and the sounds are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVATacScan.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVATacScan.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVATacScan          # play it; --no-audio for silence
TACSCAN_SHOTS=$PWD/Images dotnet test tests/AVATacScan.Tests --filter ScreenshotTests   # refresh the README's pictures
TACSCAN_PROBE=1 dotnet test tests/AVATacScan.Tests --filter PilotProbe   # how the computer pilot gets on, for tuning
python3 tools/make_sounds.py                 # rebuild src/AVATacScan/Sounds (standard-library Python only)
```

`TACSCAN_PROBE` can also be a round number to start there, and `TACSCAN_SEEDS` (default `1,2,3`) picks the games.
Without `TACSCAN_PROBE` the probe test does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVATacScan/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores), decides which part of a round runs
    (flat waves, tunnel, mother ship) and runs the fixed-step loop.
  - `Sim.cs` is the rules, one 60th of a second at a time: the squadron moving and firing together, the flat
    waves, the tunnel and the mother ship.
  - `Field.cs` has `Controls` and the measures and screen geometry (`Field`): the track from -1 to 1, the arc on the
    flat and the tube in the tunnel.
  - `World.cs` has the squadron's ships, shots, enemies, what comes down the tunnel, the mother ship, and `World`.
  - `Pilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys and mouse) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVATacScan/Views/`: `GameCanvas` (split over two partial files) draws the fixed 1024x768 screen and runs the
  60 Hz timer.
- `src/AVATacScan/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVATacScan.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `Harness.cs` sets up worlds and games.
  There are engine, gameplay, tunnel, input, pilot, shift-release and screenshot tests, and the pilot probe.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the simulation in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate, so tests play deterministically from a seed. Draw random numbers from the world's seeded `Rng`
  (`w.Rng`, set by `Sim` from the seed), never from `Random.Shared`. The only other `Random` is the star field's
  fixed `_starRng` in `GameCanvas.Draw.cs`, which is drawing only.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** The squadron only moves along its track, so `Controls` has no `dy`: `Spin`
  runs from -1 to 1 with right +1, and with `Steer` set, `Target` is a place on the track for the leader to fly to
  (the mouse and the computer player). Test new controls through `InputHandler`, as `InputTests` does, not by
  setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVATacScan`); tests load the
  table from a temporary folder (`Harness.TempFolder()`) and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
