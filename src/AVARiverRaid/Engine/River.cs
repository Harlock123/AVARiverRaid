namespace AVARiverRaid.Engine;

/// <summary>The screen: the river scrolling down under the plane, and the gauges along the bottom.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double PlayHeight = 680;
    /// <summary>Where the plane flies, up the screen.</summary>
    public const double PlaneY = 590;
    /// <summary>The river is laid out in rows this deep.</summary>
    public const double Row = 32;
}

/// <summary>One row of the river: its banks, and an island in the middle if there is one (0 wide if not).</summary>
public readonly record struct RowDef(double Left, double Right, double IslandLeft, double IslandRight)
{
    public bool Water(double x) => x > Left && x < Right && !(x > IslandLeft && x < IslandRight);
}

public enum Kind { Ship, Heli, Jet, Fuel, House }

/// <summary>Something on (or over) the river: where it starts, and how far it patrols.</summary>
public sealed record ThingDef(Kind Kind, double X, double Y, double Min, double Max, int Dir);

/// <summary>
/// The river: section after section, each ending at a bridge. The banks wind and the river narrows and widens,
/// islands split it, and along it wait ships, helicopters, jets, and the fuel depots the plane can't do without.
/// </summary>
public sealed class RiverDef
{
    public const int SectionRows = 64;
    public List<RowDef> Rows = [];
    public List<ThingDef> Things = [];
    /// <summary>The row each bridge is on.</summary>
    public List<int> Bridges = [];

    public RowDef At(int row) => row < 0 ? Rows[0] : row >= Rows.Count ? Rows[^1] : Rows[row];
    public RowDef AtY(double y) => At((int)Math.Floor(y / Field.Row));
    public int Section(double y) => Math.Clamp((int)Math.Floor(y / Field.Row) / SectionRows, 0, Bridges.Count);
    public double SectionStart(int s) => s * SectionRows * Field.Row;
}

public static class RiverBook
{
    public const int Sections = 48;

    private static RiverDef? _river;
    private static readonly Lock Gate = new();

    public static RiverDef River
    {
        get
        {
            lock (Gate) return _river ??= Build(1982);
        }
    }

    /// <summary>Points for shooting each kind; a bridge is worth 500.</summary>
    public static int Points(Kind k) => k switch { Kind.Ship => 30, Kind.Heli => 60, Kind.Jet => 100, Kind.Fuel => 80, _ => 0 };
    public const int BridgePoints = 500;

    public static RiverDef Build(int seed)
    {
        var dice = new Dice(seed);
        var def = new RiverDef();
        double centre = Field.Width / 2, width = 520;
        for (int s = 0; s < Sections; s++)
        {
            int start = s * RiverDef.SectionRows;
            double hard = Math.Min(1, s / 16.0);
            for (int i = 0; i < RiverDef.SectionRows; i++)
            {
                int row = start + i;
                // Into the bridge: the river narrows to pass under the road.
                bool approach = i >= RiverDef.SectionRows - 7 || i < 4;
                if (approach)
                {
                    centre += (Field.Width / 2 - centre) * 0.3;
                    width += (220 - width) * 0.35;
                }
                else if (i % 4 == 0)
                {
                    width = Math.Clamp(width + (dice.Next() - 0.5) * 220, 230 - hard * 30, 760 - hard * 120);
                    centre = Math.Clamp(centre + (dice.Next() - 0.5) * 160, 140 + width / 2, Field.Width - 140 - width / 2);
                }
                double il = 0, ir = 0;
                // An island down the middle of a wide stretch.
                if (!approach && width > 520 && (row / 12) % 3 == 1)
                {
                    double half = Math.Min(140, (width - 360) / 2);
                    il = centre - half;
                    ir = centre + half;
                }
                def.Rows.Add(new RowDef(centre - width / 2, centre + width / 2, il, ir));
            }
            def.Bridges.Add(start + RiverDef.SectionRows - 2);
            // What waits along it.
            int things = 6 + (int)(hard * 6) + dice.Next(3);
            for (int k = 0; k < things; k++)
            {
                int row = start + 8 + dice.Next(RiverDef.SectionRows - 18);
                var r = def.Rows[row];
                double y = (row + 0.5) * Field.Row;
                int roll = dice.Next(100);
                var kind = roll < 30 ? Kind.Ship : roll < 55 ? Kind.Heli : roll < 67 && s > 0 ? Kind.Jet : Kind.Fuel;
                // A stretch of water to patrol: the side of any island the spot is on.
                double lo = r.Left + 30, hi = r.Right - 30;
                double x = lo + dice.Next() * (hi - lo);
                if (r.IslandRight > r.IslandLeft)
                {
                    if (x < (r.IslandLeft + r.IslandRight) / 2) { hi = r.IslandLeft - 30; x = Math.Min(x, hi); }
                    else { lo = r.IslandRight + 30; x = Math.Max(x, lo); }
                }
                if (hi - lo < 40) continue;
                def.Things.Add(new ThingDef(kind, x, y, lo, hi, dice.Next(2) == 0 ? -1 : 1));
            }
            // Two fuel depots in every section, a third and two thirds of the way along, whatever else there is.
            foreach (int at in new[] { start + 20, start + 42 })
            {
                var r = def.Rows[at];
                double lo = r.Left + 40, hi = r.Right - 40;
                if (r.IslandRight > r.IslandLeft) hi = r.IslandLeft - 40;
                if (hi > lo) def.Things.Add(new ThingDef(Kind.Fuel, lo + dice.Next() * (hi - lo), (at + 0.5) * Field.Row, lo, hi, 1));
            }
            // Houses on the banks, for the look of it.
            for (int k = 0; k < 4; k++)
            {
                int row = start + 4 + dice.Next(RiverDef.SectionRows - 8);
                var r = def.Rows[row];
                bool left = dice.Next(2) == 0;
                double x = left ? r.Left - 70 - dice.Next(Math.Max(1, (int)r.Left - 120)) : r.Right + 70 + dice.Next(Math.Max(1, (int)(Field.Width - r.Right) - 120));
                if (x > 30 && x < Field.Width - 30) def.Things.Add(new ThingDef(Kind.House, x, (row + 0.5) * Field.Row, x, x, 1));
            }
        }
        def.Things.Sort((a, b) => a.Y.CompareTo(b.Y));
        return def;
    }
}
