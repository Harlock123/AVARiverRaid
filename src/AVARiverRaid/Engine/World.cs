namespace AVARiverRaid.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The controls: left or right; up to fly faster, down to slow down; fire.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>Something on the river as it is now: where, which way, whether it's still there.</summary>
public sealed class Thing
{
    public ThingDef Def = null!;
    public double X, Y;
    public int Dir;
    public bool Alive = true;
    public Thing Copy() => (Thing)MemberwiseClone();
}

public sealed record Blast(double X, double Y, double Size)
{
    public double T;
}

/// <summary>
/// Everything on the river that moves or changes, kept together so it can be copied whole: the plane (how far up the
/// river, across it, how fast, its fuel and its missile), what's on the river, the bridges down, the score.
/// </summary>
public sealed class World
{
    public RiverDef River = null!;
    /// <summary>How far up the river the plane is (its middle), and across it.</summary>
    public double D, X, Speed = Sim.Cruise;
    public double Fuel = Sim.FuelMax;
    public (double X, double Y)? Missile;
    public bool FirePrev, Dead, Refuelling;
    /// <summary>What's on the stretch of river near the plane; the next of the river's things to come into it.</summary>
    public List<Thing> Things = [];
    public int NextThing;
    public bool[] BridgeDown = [];
    public int Score;
    public double Clock;
    public string Cause = "";
    public List<Blast> Blasts = [];
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        River = River, D = D, X = X, Speed = Speed, Fuel = Fuel, Missile = Missile, FirePrev = FirePrev, Dead = Dead, Refuelling = Refuelling,
        Things = Things.Select(t => t.Copy()).ToList(), NextThing = NextThing, BridgeDown = [.. BridgeDown], Score = Score, Clock = Clock, Cause = Cause,
    };

    public int Section => River.Section(D);
}
