namespace AVARiverRaid.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. Each moment it tries every way of flying (left, right or
/// straight; slow, cruising or fast) for a second ahead on a copy of the river, and takes the one that comes through
/// furthest, clear of the banks, and over a fuel depot when the tank is getting low. It fires at anything ahead in its
/// line, bridges included, but leaves the fuel depots alone unless the tank is nearly full.
/// </summary>
public sealed class Pilot
{
    /// <summary>Each try is two moves, one after the other: the first this many steps, the second <see cref="Then"/>.</summary>
    public const int First = 24, Then = 42;
    /// <summary>It thinks again every few steps, holding its course in between.</summary>
    public const int Every = 3;
    private (int Dx, int Dy) _last;
    private int _wait;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _last = (0, 0);
        _wait = 0;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Dead) return default;
        if (_wait-- <= 0)
        {
            _wait = Every - 1;
            double best = double.NegativeInfinity;
            var pick = _last;
            foreach (int dx in new[] { -1, 0, 1 })
                foreach (int dy in new[] { -1, 0, 1 })
                {
                    double v = Value(w, dx, dy);
                    if ((dx, dy) == _last) v += 2;
                    if (v > best) { best = v; pick = (dx, dy); }
                }
            _last = pick;
            Doing = w.Fuel < 70 ? "looking for fuel" : best < -1e5 ? "in trouble" : "up the river";
        }
        return new Controls(_last.Dx, _last.Dy, Shoot(w) && !w.FirePrev);
    }

    /// <summary>One way of flying, then the best of the ways after it: how well it comes out.</summary>
    private static double Value(World w, int dx, int dy)
    {
        var c = w.Copy();
        for (int i = 0; i < First; i++)
        {
            Sim.Step(c, new Controls(dx, dy, Shoot(c) && !c.FirePrev));
            if (c.Dead) return -1e6 + i * 1000;
        }
        double best = double.NegativeInfinity;
        foreach (int dx2 in new[] { -1, 0, 1 })
            foreach (int dy2 in new[] { 0, 1 })
                best = Math.Max(best, Then2(w, c.Copy(), dx2, dy2));
        return best;
    }

    private static double Then2(World w, World c, int dx, int dy)
    {
        double fuel0 = w.Fuel;
        for (int i = 0; i < Then; i++)
        {
            Sim.Step(c, new Controls(dx, dy, Shoot(c) && !c.FirePrev));
            if (c.Dead) return -1e6 + (First + i) * 1000;
        }
        double v = c.D - w.D;
        // Room either side.
        var r = c.River.AtY(c.D + 60);
        double room = Math.Min(c.X - r.Left, r.Right - c.X);
        if (r.IslandRight > r.IslandLeft) room = Math.Min(room, Math.Abs(c.X - (r.IslandLeft + r.IslandRight) / 2) - (r.IslandRight - r.IslandLeft) / 2);
        v += Math.Clamp(room, -50, 140) * 0.8;
        if (w.Fuel < 70)
        {
            // Low on fuel: make for the next depot, and fill up there slowly.
            v += (c.Fuel - fuel0) * 30;
            var depot = c.Things.Where(t => t.Alive && t.Def.Kind == Kind.Fuel && t.Y > c.D - 30 && t.Y - c.D < 900).OrderBy(t => t.Y).FirstOrDefault();
            if (depot != null) v -= Math.Abs(depot.X - c.X) * (w.Fuel < 40 ? 2.5 : 1.2);
        }
        return v;
    }

    /// <summary>Something worth a missile in line ahead.</summary>
    private static bool Shoot(World w)
    {
        if (w.Missile != null) return false;
        foreach (var t in w.Things)
        {
            if (!t.Alive || t.Def.Kind == Kind.House || t.Def.Kind == Kind.Fuel && w.Fuel < 80) continue;
            double ahead = t.Y - w.D;
            if (ahead > 0 && ahead < 520 && Math.Abs(t.X - w.X) < Sim.Size(t.Def.Kind).Hw + 4) return true;
        }
        int b = w.Section;
        if (b < w.BridgeDown.Length && !w.BridgeDown[b])
        {
            double ahead = w.River.Bridges[b] * Field.Row - w.D;
            if (ahead > 0 && ahead < 520 && w.River.At(w.River.Bridges[b]).Water(w.X)) return true;
        }
        return false;
    }
}
