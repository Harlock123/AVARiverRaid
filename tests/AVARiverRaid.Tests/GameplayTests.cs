using Avalonia.Input;
using AVARiverRaid.Engine;

namespace AVARiverRaid.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avariverraid-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A straight river (300 to 724) two sections long, with the things given.</summary>
    private static World Straight(params ThingDef[] things)
    {
        var def = new RiverDef();
        for (int i = 0; i < 2 * RiverDef.SectionRows; i++) def.Rows.Add(new RowDef(300, 724, 0, 0));
        def.Bridges.Add(RiverDef.SectionRows - 2);
        def.Bridges.Add(2 * RiverDef.SectionRows - 2);
        def.Things.AddRange(things.OrderBy(t => t.Y));
        var w = new World();
        Sim.Start(w, def);
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    // ───────────────────────────── the river ─────────────────────────────

    [Fact]
    public void The_river_is_the_same_from_the_same_seed_with_a_bridge_ending_each_section()
    {
        var a = RiverBook.Build(7);
        var b = RiverBook.Build(7);
        Assert.Equal(a.Rows, b.Rows);
        var r = RiverBook.River;
        Assert.Equal(RiverBook.Sections, r.Bridges.Count);
        for (int s = 0; s < r.Bridges.Count; s++)
        {
            Assert.Equal(s, r.Bridges[s] / RiverDef.SectionRows);
            Assert.True(r.At(r.Bridges[s]).Right - r.At(r.Bridges[s]).Left < 300);
        }
    }

    [Fact]
    public void Every_section_has_fuel_and_room_to_fly()
    {
        var r = RiverBook.River;
        for (int s = 0; s < RiverBook.Sections; s++)
        {
            double y0 = r.SectionStart(s), y1 = r.SectionStart(s + 1);
            Assert.True(r.Things.Count(t => t.Kind == Kind.Fuel && t.Y >= y0 && t.Y < y1) >= 2, $"section {s}");
        }
        foreach (var row in r.Rows)
        {
            double widest = Math.Max(row.Right - row.Left - Math.Max(0, row.IslandRight - row.IslandLeft), 0);
            Assert.True(widest > 100);
        }
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_plane_flies_up_the_river_faster_or_slower_and_steers()
    {
        var w = Straight();
        double d = w.D;
        Run(w, 60);
        Assert.Equal(d + Sim.Cruise, w.D, 1);
        Run(w, 60, new Controls(0, -1));
        Assert.Equal(d + Sim.Cruise + Sim.Fast, w.D, 1);
        Run(w, 60, new Controls(0, 1));
        Assert.Equal(d + Sim.Cruise + Sim.Fast + Sim.Slow, w.D, 1);
        double x = w.X;
        Run(w, 30, new Controls(1));
        Assert.Equal(x + Sim.Across / 2, w.X, 1);
    }

    [Fact]
    public void Into_the_bank_is_a_crash()
    {
        var w = Straight();
        Run(w, 120, new Controls(1));
        Assert.True(w.Dead);
        Assert.Equal("the bank", w.Cause);
    }

    [Fact]
    public void Fuel_runs_down_and_out_is_a_crash_but_a_depot_fills_the_tank()
    {
        var w = Straight();
        Run(w, 60);
        Assert.Equal(Sim.FuelMax - Sim.Burn, w.Fuel, 1);
        w.Fuel = 1;
        Run(w, 30);
        Assert.True(w.Dead);
        Assert.Equal("out of fuel", w.Cause);

        var v = Straight(new ThingDef(Kind.Fuel, 512, 400, 512, 512, 1));
        v.Fuel = 30;
        Run(v, 300, new Controls(0, 1));
        Assert.False(v.Dead);
        Assert.True(v.Fuel > 50);
    }

    [Fact]
    public void A_ship_patrols_once_the_plane_is_near_and_is_a_crash_to_hit()
    {
        var w = Straight(new ThingDef(Kind.Ship, 512, 600, 340, 680, 1));
        double x = w.Things[0].X;
        Run(w, 30);
        Assert.NotEqual(x, w.Things[0].X);
        Run(w, 400);
        Assert.True(w.Dead || w.Things[0].Y < w.D);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void A_missile_sinks_a_ship_and_scores()
    {
        var w = Straight(new ThingDef(Kind.Ship, 512, 600, 512, 512, 1));
        Sim.Step(w, new Controls(Fire: true));
        Assert.NotNull(w.Missile);
        Run(w, 40);
        Assert.False(w.Things[0].Alive);
        Assert.Equal(RiverBook.Points(Kind.Ship), w.Score);
    }

    [Fact]
    public void A_bridge_must_be_shot_down_to_pass()
    {
        var w = Straight();
        w.D = (RiverDef.SectionRows - 6) * Field.Row;
        Run(w, 120);
        Assert.True(w.Dead);
        Assert.Equal("the bridge", w.Cause);

        var v = Straight();
        v.D = (RiverDef.SectionRows - 10) * Field.Row;
        Sim.Step(v, new Controls(Fire: true));
        Run(v, 200);
        Assert.False(v.Dead);
        Assert.True(v.BridgeDown[0]);
        Assert.Equal(RiverBook.BridgePoints, v.Score);
        Assert.Equal(1, v.Section);
    }

    [Fact]
    public void A_crash_comes_back_at_the_start_of_the_section_with_its_things_as_they_were()
    {
        var w = Straight(new ThingDef(Kind.Ship, 512, 2400, 512, 512, 1));
        w.D = RiverDef.SectionRows * Field.Row + 100;
        w.BridgeDown[0] = true;
        Sim.Step(w, default);
        w.Things.First().Alive = false;
        w.Dead = true;
        Sim.Respawn(w, w.Section);
        Assert.Equal(RiverDef.SectionRows * Field.Row + 80, w.D);
        Assert.True(w.Things.First(t => t.Def.Kind == Kind.Ship).Alive);
        Assert.True(w.BridgeDown[0]);
        Assert.False(w.BridgeDown[1]);
        Assert.Equal(Sim.FuelMax, w.Fuel);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Lives_run_out_and_the_game_is_over_then_initials()
    {
        var g = Engine();
        g.Go();
        g.World.Score = 9000;
        for (int i = 0; i < 20 && g.State == GameState.Playing; i++)
        {
            g.World.Dead = true;
            g.Run((int)(60 * (GameEngine.DyingSeconds + GameEngine.StartSeconds)) + 10);
        }
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_flying_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 5) && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_the_plane()
    {
        var g = Engine();
        g.Go();
        g.Run(30);
        double d = g.World.D;
        g.Pause();
        g.Run(200);
        Assert.Equal(d, g.World.D);
        g.Resume();
        g.Run(10);
        Assert.True(g.World.D > d);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = Straight(new ThingDef(Kind.Ship, 512, 600, 512, 512, 1));
        var c = w.Copy();
        Sim.Step(c, new Controls(Fire: true));
        Run(c, 40);
        Assert.True(w.Things[0].Alive);
        Assert.False(c.Things[0].Alive);
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.Space);
        Assert.Equal(default, input.Player);
        input.Dispose();

        var g = Engine();
        g.Go();
        double x = g.World.X;
        g.Run(20, new Controls(1));
        Assert.True(g.World.X > x + 60);
    }

    // ───────────────────────────── the computer pilot ─────────────────────────────

    [Fact]
    public void The_computer_flies_ten_bridges_without_a_crash_and_flying_straight_does_not()
    {
        var w = new World();
        Sim.Start(w, RiverBook.River);
        var p = new Pilot();
        for (int t = 0; t < 60 * 200 && !w.Dead && w.Section < 10; t++) Sim.Step(w, p.Decide(w));
        Assert.False(w.Dead, w.Cause);
        Assert.Equal(10, w.Section);

        var lazy = new World();
        Sim.Start(lazy, RiverBook.River);
        for (int t = 0; t < 60 * 200 && !lazy.Dead; t++) Sim.Step(lazy, new Controls(0, 0, t % 10 == 0));
        Assert.True(lazy.Dead);
        Assert.Equal(0, lazy.Section);
    }
}
