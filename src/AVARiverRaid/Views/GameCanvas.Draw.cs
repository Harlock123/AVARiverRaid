using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARiverRaid.Engine;

namespace AVARiverRaid.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARiverRaid/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const uint Grass = 0xFF3A8A2E, GrassDark = 0xFF2E7224, Water = 0xFF2A5AC8, Ripple = 0xFF5A8AE8;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawRiver(dc, g.World, 3000 + _clock * 60, false); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawRiver(dc, g.World, 6000, false); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawRiver(dc, g.World, g.World.D, true);
                DrawGauges(dc, g.World);
                if (g.Message is { } msg && g.Phase is Phase.Start)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(0, 300, Field.Width, 70));
                    Text(dc, msg, new Point(512, 335), 52, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 20), 22, Yellow, center: true);
    }

    private static double ScreenY(double y, double d) => Field.PlaneY - (y - d);

    /// <summary>The river from the air, scrolled to a point, and (in play) everything on it.</summary>
    private void DrawRiver(DrawingContext dc, World w, double d, bool things)
    {
        var river = RiverBook.River;
        dc.FillRectangle(Brush(Grass), new Rect(0, 0, Field.Width, Field.PlayHeight));
        int r0 = (int)Math.Floor((d - (Field.PlayHeight - Field.PlaneY)) / Field.Row) - 1, r1 = (int)Math.Ceiling((d + Field.PlaneY) / Field.Row) + 1;
        for (int r = r0; r <= r1; r++)
        {
            var row = river.At(r);
            double sy = ScreenY((r + 1) * Field.Row, d);
            dc.FillRectangle(Brush(Water), new Rect(row.Left, sy, row.Right - row.Left, Field.Row + 1));
            if (row.IslandRight > row.IslandLeft) dc.FillRectangle(Brush(Grass), new Rect(row.IslandLeft, sy, row.IslandRight - row.IslandLeft, Field.Row + 1));
            // Ripples, and trees on the land.
            uint h = Hash(r, 1, 7);
            dc.DrawLine(Pen(Ripple, 2), new Point(row.Left + 20 + h % (uint)Math.Max(1, row.Right - row.Left - 60), sy + 12), new Point(row.Left + 44 + h % (uint)Math.Max(1, row.Right - row.Left - 60), sy + 12));
            for (int k = 0; k < 2; k++)
            {
                double tx = Hash(r, k, 9) % 1024;
                if (!row.Water(tx) && Math.Abs(tx - row.Left) > 16 && Math.Abs(tx - row.Right) > 16) { dc.FillEllipse(tx, sy + 16, 9, GrassDark); dc.FillEllipse(tx - 2, sy + 13, 4, 0xFF4AA03A); }
            }
        }
        // The bridges, and the road over the land to them.
        for (int b = 0; b < river.Bridges.Count; b++)
        {
            int row = river.Bridges[b];
            double sy = ScreenY((row + 1) * Field.Row, d);
            if (sy < -40 || sy > Field.PlayHeight + 40) continue;
            dc.FillRectangle(Brush(0xFF6A6A6A), new Rect(0, sy + 4, Field.Width, Field.Row - 8));
            for (double x = 10; x < Field.Width; x += 50) dc.FillRectangle(Brush(0xFFE0D040), new Rect(x, sy + 14, 26, 4));
            var rr = river.At(row);
            bool down = things && w.BridgeDown.Length > b && w.BridgeDown[b];
            if (down) dc.FillRectangle(Brush(Water), new Rect(rr.Left, sy, rr.Right - rr.Left, Field.Row));
            else
            {
                dc.FillRectangle(Brush(0xFF8A7A6A), new Rect(rr.Left - 6, sy, rr.Right - rr.Left + 12, Field.Row));
                for (double x = rr.Left; x < rr.Right; x += 24) dc.DrawLine(Pen(0xFF5A4A3A, 2), new Point(x, sy), new Point(x, sy + Field.Row));
            }
        }
        if (!things) return;
        foreach (var t in w.Things)
        {
            double sy = ScreenY(t.Y, d);
            if (sy < -60 || sy > Field.PlayHeight + 60) continue;
            if (t.Alive || t.Def.Kind == Kind.House) DrawThing(dc, t.Def.Kind, t.X, sy, t.Dir);
        }
        if (w.Missile is { } m) dc.DrawLine(Pen(0xFFFFF080, 4), new Point(m.X, ScreenY(m.Y, d)), new Point(m.X, ScreenY(m.Y, d) + 18));
        foreach (var b in w.Blasts)
        {
            double k = b.T / 0.6, sy = ScreenY(b.Y, d);
            dc.FillEllipse(b.X, sy, b.Size * (0.5 + k), Alpha(0xFFFF8020, 1 - k));
            dc.FillEllipse(b.X, sy, b.Size * (0.25 + k * 0.6), Alpha(0xFFFFF0A0, 1 - k));
        }
        if (!w.Dead) DrawPlane(dc, w.X, Field.PlaneY, w.Speed);
    }

    private void DrawThing(DrawingContext dc, Kind k, double x, double y, int dir)
    {
        switch (k)
        {
            case Kind.Ship:
                Poly(dc, 0xFF404850, 0xFF202428, 2, new Point(x - 34 * dir, y - 10), new Point(x + 26 * dir, y - 10), new Point(x + 36 * dir, y), new Point(x + 26 * dir, y + 10), new Point(x - 34 * dir, y + 10));
                dc.FillRectangle(Brush(0xFFC0C0C0), new Rect(x - 18, y - 6, 16, 12), 2);
                dc.FillRectangle(Brush(0xFFC03030), new Rect(x + 4, y - 4, 10, 8));
                break;
            case Kind.Heli:
            {
                dc.DrawEllipse(Brush(0xFF2A6A2A), Pen(0xFF103010, 2), new Point(x, y), 16, 10);
                dc.DrawLine(Pen(0xFF2A6A2A, 4), new Point(x - 14 * dir, y), new Point(x - 30 * dir, y - 2));
                double a = _clock * 30;
                dc.DrawLine(Pen(0xFF101010, 3), new Point(x - Math.Cos(a) * 26, y - Math.Sin(a) * 8), new Point(x + Math.Cos(a) * 26, y + Math.Sin(a) * 8));
                break;
            }
            case Kind.Jet:
                Poly(dc, 0xFF5A5A70, 0xFF202030, 2, new Point(x + 26 * dir, y), new Point(x - 20 * dir, y - 18), new Point(x - 14 * dir, y), new Point(x - 20 * dir, y + 18));
                break;
            case Kind.Fuel:
                dc.FillRectangle(Brush(0xFFE8E8E8), new Rect(x - 22, y - 34, 44, 68), 4);
                for (int i = 0; i < 4; i++) dc.FillRectangle(Brush(0xFFD02020), new Rect(x - 22, y - 34 + i * 17, 44, 8));
                Text(dc, "F", new Point(x, y - 26), 16, 0xFF202020, center: true, shadow: false);
                Text(dc, "U", new Point(x, y - 9), 16, 0xFF202020, center: true, shadow: false);
                Text(dc, "E", new Point(x, y + 8), 16, 0xFF202020, center: true, shadow: false);
                Text(dc, "L", new Point(x, y + 25), 16, 0xFF202020, center: true, shadow: false);
                break;
            case Kind.House:
                dc.FillRectangle(Brush(0xFFE0D0B0), new Rect(x - 20, y - 12, 40, 24));
                Poly(dc, 0xFFB03020, null, 0, new Point(x - 24, y - 12), new Point(x, y - 26), new Point(x + 24, y - 12));
                dc.FillRectangle(Brush(0xFF6A4020), new Rect(x - 4, y, 8, 12));
                break;
        }
    }

    private void DrawPlane(DrawingContext dc, double x, double y, double speed)
    {
        // The jet from above, with its exhaust burning harder the faster it goes.
        double flame = 8 + (speed - Sim.Slow) / 10 + Math.Sin(_clock * 40) * 2;
        Poly(dc, 0xFFFF8020, null, 0, new Point(x - 5, y + 18), new Point(x, y + 18 + flame), new Point(x + 5, y + 18));
        Poly(dc, 0xFFE8D040, 0xFF806010, 2, new Point(x, y - 22), new Point(x + 5, y - 6), new Point(x + 22, y + 6), new Point(x + 22, y + 11), new Point(x + 5, y + 6),
            new Point(x + 4, y + 14), new Point(x + 10, y + 20), new Point(x - 10, y + 20), new Point(x - 4, y + 14), new Point(x - 5, y + 6), new Point(x - 22, y + 11), new Point(x - 22, y + 6), new Point(x - 5, y - 6));
        dc.FillEllipse(x, y - 8, 3, 0xFF40A0E0);
    }

    private void DrawGauges(DrawingContext dc, World w)
    {
        var g = Engine;
        double top = Field.PlayHeight;
        dc.FillRectangle(Brush(0xFF6A6A6A), new Rect(0, top, Field.Width, Field.Height - top));
        dc.FillRectangle(Brush(0xFF4A4A4A), new Rect(0, top, Field.Width, 4));
        Text(dc, $"{g.Score}", new Point(512, top + 26), 40, 0xFFE0D040, center: true);
        // The fuel gauge: E, a half, F, and the needle.
        var box = new Rect(380, top + 50, 264, 30);
        dc.FillRectangle(Brush(0xFF202020), box, 4);
        Text(dc, "E", new Point(box.X + 14, box.Y + 15), 22, White, center: true, shadow: false);
        Text(dc, "½", new Point(box.X + box.Width / 2, box.Y + 15), 22, White, center: true, shadow: false);
        Text(dc, "F", new Point(box.Right - 14, box.Y + 15), 22, White, center: true, shadow: false);
        double nx = box.X + 14 + (box.Width - 28) * w.Fuel / Sim.FuelMax;
        dc.DrawLine(Pen(w.Fuel < 25 && (int)(_clock * 4) % 2 == 0 ? Red : 0xFFF0F0F0, 4), new Point(nx, box.Y + 4), new Point(nx, box.Bottom - 4));
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++) DrawMiniPlane(dc, 40 + i * 40, top + 60);
        Text(dc, $"BRIDGE {g.Bridge}", new Point(900, top + 60), 30, White, center: true);
    }

    private static void DrawMiniPlane(DrawingContext dc, double x, double y)
    {
        Poly(dc, 0xFFE8D040, 0xFF806010, 1, new Point(x, y - 12), new Point(x + 12, y + 4), new Point(x + 3, y + 3), new Point(x + 5, y + 10), new Point(x - 5, y + 10),
            new Point(x - 3, y + 3), new Point(x - 12, y + 4));
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawRiver(dc, Engine.World, 200 + _clock * 120, false);
        DrawPlane(dc, cx + Math.Sin(_clock * 0.8) * 60, 560, Sim.Cruise);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 410, 60, 820, 340), 12);
        string title = "AVA RIVER RAID";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 120 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 112, i < 3 ? 0xFF40A0E0 : 0xFFE0D040, center: true);
        }
        Text(dc, "AFTER CAROL SHAW'S 1982 GAME", new Point(cx, 186), 26, White, center: true);
        Text(dc, "FLY UP THE RIVER AS FAR AS YOU CAN. SHOOT THE SHIPS, HELICOPTERS", new Point(cx, 236), 24, White, center: true);
        Text(dc, "AND JETS, AND THE BRIDGES THAT BAR THE WAY. KEEP OFF THE BANKS.", new Point(cx, 264), 24, White, center: true);
        Text(dc, "YOUR FUEL RUNS DOWN ALL THE TIME: FLY LOW OVER THE FUEL DEPOTS TO", new Point(cx, 292), 24, White, center: true);
        Text(dc, "FILL UP (OR SHOOT THEM FOR POINTS, IF YOU DARE).", new Point(cx, 320), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 370), 40, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "LEFT/RIGHT: STEER   UP: FASTER   DOWN: SLOWER   SPACE: FIRE   P: PAUSE   M: SOUND", new Point(cx, 718), 20, 0xFFE0E0C0, center: true);
        Text(dc, "MUSIC: MARCHES OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawRiver(dc, g.World, g.World.D, false);
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 360, 150, 720, 230), 12);
        Text(dc, "GAME OVER", new Point(cx, 220), 100, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 300), 46, White, center: true);
        Text(dc, $"YOU REACHED BRIDGE {g.Bridge}", new Point(cx, 350), 30, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BOLDEST RAIDERS", new Point(cx, 120), 58, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  BRIDGE", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
