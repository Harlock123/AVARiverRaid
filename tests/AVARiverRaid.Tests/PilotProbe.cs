using AVARiverRaid.Engine;

namespace AVARiverRaid.Tests;

/// <summary>How the computer pilot gets on (set RIVER_PROBE to run it; RIVER_PROBE=lazy flies straight and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("RIVER_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = new World();
        Sim.Start(w, RiverBook.River);
        var p = new Pilot();
        var deaths = new List<string>();
        int t = 0;
        double minFuel = 100;
        for (; t < 60 * 60 * 8 && deaths.Count < 8 && w.Section < RiverBook.Sections - 1; t++)
        {
            var c = mode == "lazy" ? new Controls(0, 0, t % 10 == 0) : p.Decide(w);
            Sim.Step(w, c);
            minFuel = Math.Min(minFuel, w.Fuel);
            if (w.Dead)
            {
                deaths.Add($"{w.Cause} at bridge {w.Section + 1} [{p.Doing}] fuel {w.Fuel:0}");
                Sim.Respawn(w, w.Section);
                p.Reset();
            }
        }
        output.WriteLine($"after {t / 60}s: bridge {w.Section + 1} of {RiverBook.Sections}, score {w.Score}, lowest fuel {minFuel:0}, {deaths.Count} crashes; {sw.ElapsedMilliseconds} ms");
        foreach (var d in deaths) output.WriteLine("   " + d);
    }
}
