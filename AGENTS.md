# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A vertically scrolling shooter in the style of Activision's 1982 River Raid, in C# on .NET 10 and Avalonia 12.
One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in
code. The sounds and marches are synthesised by `tools/make_sounds.py`, and the low-fuel call by the formant
voice in `tools/speech.py`.

## Commands

```bash
dotnet build AVARiverRaid.slnx                    # the SDK version is in global.json (10.0.400 or later)
dotnet test AVARiverRaid.slnx                     # every test runs headless: no window opens
dotnet run --project src/AVARiverRaid             # play it; --no-audio for silence
RIVER_SHOTS=$PWD/Images dotnet test tests/AVARiverRaid.Tests --filter ScreenshotTests   # refresh the README's pictures
RIVER_PROBE=1 dotnet test tests/AVARiverRaid.Tests --filter PilotProbe   # report how the computer pilot does (RIVER_PROBE=lazy: straight ahead, firing)
python3 tools/make_sounds.py                      # rebuild src/AVARiverRaid/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVARiverRaid/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores), lives, restarts from the
    last bridge and the fixed-step loop.
  - `Sim.cs` holds the rules, one step at a time: the plane, fuel, shots, the ships, helicopters and jets.
  - `River.cs` has `Field` (the screen) and the river's definition. `RiverBook.Build` lays out every section,
    and `RiverBook` keeps the one river the game uses, built from seed 1982.
  - `World.cs` has the `Dice` generator, `Controls` and the live state of things on the river.
  - `Pilot.cs` is the computer pilot, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVARiverRaid/Views/`: `GameCanvas` draws the river and the gauges (`GameCanvas.Draw.cs`) and runs the
  timer.
- `src/AVARiverRaid/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVARiverRaid/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVARiverRaid.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots and the
  `PilotProbe` tuning report.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`Field.Step`), so tests can fly whole sections deterministically.
- **One river, the same every game.** The only randomness is the seeded `Dice` that `RiverBook.Build` uses to
  lay out the river. The simulation itself draws no random numbers. Don't add `Random` or `Random.Shared`; if
  something needs chance, take it from a seeded `Dice`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls.Dy` is -1 for up (fly faster) and 1 for down (slow down).
  Keys, D-pad and stick must all keep that sign.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVARiverRaid`); tests
  load the table from a fresh temp folder and set `Silent = true` on the engine.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- A change to `RiverBook.Build` changes the whole river: run the probe to check the computer pilot still gets
  through, and refresh the screenshots.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
