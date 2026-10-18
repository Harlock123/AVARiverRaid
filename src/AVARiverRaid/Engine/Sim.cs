namespace AVARiverRaid.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const double Slow = 90, Cruise = 150, Fast = 250, Across = 270;
    public const double FuelMax = 100, Burn = 3.6, Refuel = 55;
    /// <summary>Things come onto the stretch near the plane this far ahead, and leave it this far behind.</summary>
    public const double Ahead = 1400, Behind = 800;
    public const double MissileSpeed = 1100, MissileReach = 620;
    public const double HalfW = 17, HalfH = 19;

    public static (double Hw, double Hh) Size(Kind k) => k switch
    {
        Kind.Ship => (34, 12), Kind.Heli => (24, 14), Kind.Jet => (26, 10), Kind.Fuel => (22, 34), _ => (24, 20),
    };

    public static double ThingSpeed(Kind k) => k switch { Kind.Ship => 55, Kind.Heli => 85, Kind.Jet => 300, _ => 0 };

    public static void Start(World w, RiverDef river)
    {
        w.River = river;
        w.BridgeDown = new bool[river.Bridges.Count];
        w.Score = 0;
        Respawn(w, 0);
    }

    /// <summary>The plane back at the start of a section, fuelled up, with the section's river as it was.</summary>
    public static void Respawn(World w, int section)
    {
        double y0 = w.River.SectionStart(section);
        w.D = y0 + 80;
        var r = w.River.AtY(w.D);
        w.X = (r.Left + r.Right) / 2;
        if (r.IslandRight > r.IslandLeft) w.X = (r.Left + r.IslandLeft) / 2;
        w.Speed = Cruise;
        w.Fuel = FuelMax;
        w.Missile = null;
        w.Dead = false;
        w.Cause = "";
        // The section's things as they were at the start.
        w.Things.Clear();
        w.NextThing = w.River.Things.FindIndex(t => t.Y >= y0 - 700);
        if (w.NextThing < 0) w.NextThing = w.River.Things.Count;
        Bring(w);
        for (int b = section; b < w.BridgeDown.Length; b++) w.BridgeDown[b] = false;
    }

    /// <summary>Whether a point is over water (an intact bridge counts as land).</summary>
    public static bool Water(World w, double x, double y)
    {
        int row = (int)Math.Floor(y / Field.Row);
        int b = w.River.Bridges.IndexOf(row);
        if (b >= 0 && !w.BridgeDown[b]) return false;
        return w.River.At(row).Water(x);
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        for (int i = w.Blasts.Count - 1; i >= 0; i--)
            if ((w.Blasts[i].T += dt) > 0.6) w.Blasts.RemoveAt(i);
        if (w.Dead) return;
        w.Speed = c.Dy < 0 ? Fast : c.Dy > 0 ? Slow : Cruise;
        w.D += w.Speed * dt;
        w.X = Math.Clamp(w.X + c.Dx * Across * dt, HalfW, Field.Width - HalfW);
        w.Fuel -= Burn * dt;
        Bring(w);
        // The fire button.
        bool fire = c.Fire && !w.FirePrev;
        w.FirePrev = c.Fire;
        if (fire && w.Missile == null) { w.Missile = (w.X, w.D + HalfH); w.Heard.Add(Sfx.Fire); }
        Things(w);
        Missile(w);
        // The banks and bridges.
        foreach (var (dx, dy) in new[] { (-HalfW, -HalfH), (HalfW, -HalfH), (-HalfW, HalfH), (HalfW, HalfH), (0, HalfH) })
            if (!Water(w, w.X + dx, w.D + dy))
            {
                Crash(w, w.River.Bridges.Contains((int)Math.Floor((w.D + dy) / Field.Row)) ? "the bridge" : "the bank");
                return;
            }
        // Over a fuel depot: filling up. Anything else is a crash.
        w.Refuelling = false;
        foreach (var t in w.Things)
        {
            if (!t.Alive || t.Def.Kind == Kind.House) continue;
            var (hw, hh) = Size(t.Def.Kind);
            if (Math.Abs(t.X - w.X) > hw + HalfW - 4 || Math.Abs(t.Y - w.D) > hh + HalfH - 4) continue;
            if (t.Def.Kind == Kind.Fuel) { w.Refuelling = true; continue; }
            Crash(w, t.Def.Kind.ToString().ToLowerInvariant());
            return;
        }
        if (w.Refuelling) w.Fuel = Math.Min(FuelMax, w.Fuel + Refuel * dt);
        if (w.Fuel <= 0) { w.Fuel = 0; Crash(w, "out of fuel"); }
    }

    /// <summary>The river's things come into play as the plane nears them, and go once it's well past.</summary>
    private static void Bring(World w)
    {
        var all = w.River.Things;
        while (w.NextThing < all.Count && all[w.NextThing].Y < w.D + Ahead)
        {
            var d = all[w.NextThing++];
            w.Things.Add(new Thing { Def = d, X = d.X, Y = d.Y, Dir = d.Dir });
        }
        w.Things.RemoveAll(t => t.Y < w.D - Behind);
    }

    /// <summary>Ships and helicopters patrol once the plane comes near; jets cross the river.</summary>
    private static void Things(World w)
    {
        foreach (var t in w.Things)
        {
            if (!t.Alive || t.Y - w.D > 520 || t.Y < w.D - 700) continue;
            double v = ThingSpeed(t.Def.Kind) * Field.Step;
            if (v == 0) continue;
            if (t.Def.Kind == Kind.Jet)
            {
                t.X += t.Dir * v;
                if (t.X < -60 || t.X > Field.Width + 60) t.X = t.Dir > 0 ? -60 : Field.Width + 60;
                continue;
            }
            t.X += t.Dir * v;
            if (t.X < t.Def.Min || t.X > t.Def.Max) { t.Dir = -t.Dir; t.X = Math.Clamp(t.X, t.Def.Min, t.Def.Max); }
        }
    }

    private static void Missile(World w)
    {
        if (w.Missile is not { } m) return;
        double y = m.Y + MissileSpeed * Field.Step;
        if (y - w.D > MissileReach) { w.Missile = null; return; }
        w.Missile = (m.X, y);
        foreach (var t in w.Things)
        {
            if (!t.Alive || t.Def.Kind == Kind.House) continue;
            var (hw, hh) = Size(t.Def.Kind);
            if (Math.Abs(t.X - m.X) < hw + 3 && t.Y - hh < y && t.Y + hh > m.Y)
            {
                t.Alive = false;
                w.Score += RiverBook.Points(t.Def.Kind);
                w.Missile = null;
                w.Blasts.Add(new Blast(t.X, t.Y, 40));
                w.Heard.Add(Sfx.Boom);
                return;
            }
        }
        // A bridge across the river (not the road over the land).
        for (int b = 0; b < w.River.Bridges.Count; b++)
        {
            if (w.BridgeDown[b]) continue;
            int row = w.River.Bridges[b];
            double by = row * Field.Row;
            if (by + Field.Row < m.Y || by > y) continue;
            if (!w.River.At(row).Water(m.X)) { w.Missile = null; return; }
            w.BridgeDown[b] = true;
            w.Score += RiverBook.BridgePoints;
            w.Missile = null;
            w.Blasts.Add(new Blast(m.X, by + Field.Row / 2, 90));
            w.Heard.Add(Sfx.Bridge);
            return;
        }
    }

    private static void Crash(World w, string cause)
    {
        w.Dead = true;
        w.Cause = cause;
        w.Blasts.Add(new Blast(w.X, w.D, 70));
        w.Heard.Add(Sfx.Crash);
    }
}
