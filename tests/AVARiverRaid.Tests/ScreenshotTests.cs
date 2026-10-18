using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARiverRaid.Engine;
using AVARiverRaid.Views;
using Controls = AVARiverRaid.Engine.Controls;

namespace AVARiverRaid.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set RIVER_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avariverraid-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("RIVER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARiverRaid-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 300)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 31260, 22);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Up_the_river() => Moment("river", g => g.World.D > 2400 && g.World.Things.Count(t => t.Alive && t.Def.Kind != Kind.House && Math.Abs(t.Y - g.World.D) < 500) >= 3);

    [AvaloniaFact]
    public void A_shot() => Moment("shot", g => g.World.Missile is { } m && m.Y - g.World.D > 120 && g.World.Things.Any(t => t.Alive && t.Def.Kind is Kind.Ship or Kind.Heli && Math.Abs(t.X - m.X) < 30 && t.Y - m.Y is > 20 and < 120));

    [AvaloniaFact]
    public void A_bridge() => Moment("bridge", g => g.World.Blasts.Any(b => b.Size > 80 && b.T is > 0.05 and < 0.2));

    [AvaloniaFact]
    public void Refuelling() => Moment("fuel", g => g.World.Refuelling && g.World.Fuel < 80);

    [AvaloniaFact]
    public void An_island() => Moment("island", g => g.World.River.AtY(g.World.D + 200) is var r && r.IslandRight > r.IslandLeft && g.World.D > 1500);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(1);
        g.World.Score = 6450;
        for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++) { g.Tick(new Controls(1)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
