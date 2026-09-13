using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>What a stretch of unexplored ground is worth going to look at.</summary>
// The two halves of that: how much dark a trip would turn over, and roughly what the trip
// is worth. Here rather than in the job that uses them because both are questions about
// ground and neither knows anything about jobs, and because this file has no game in it,
// so the harness can check the arithmetic without a world.
internal static class Prospect
{
    /// <summary>How far a torch reaches, which is what arriving at a tile turns over.</summary>
    private const int Lit = 10;

    /// <summary>What a tile of rock is worth against a tile of walking.</summary>
    // A copper pickaxe spends about eleven walking ticks on a tile of stone.
    private const int Rock = 11;

    /// <summary>How much unseen ground stands to be revealed by going to a tile.</summary>
    // Every other tile, because this is asked of every candidate on every choice and a
    // quarter of the reads gives the same ordering: what is being compared is a hundred
    // dark cells against four, not a hundred against ninety six.
    public static int Unseen(ITerrain terrain, Point at)
    {
        int dark = 0;
        for (int across = -Lit; across <= Lit; across += 2)
        {
            for (int down = -Lit; down <= Lit; down += 2)
            {
                if (!terrain.IsKnown(at.X + across, at.Y + down))
                {
                    dark++;
                }
            }
        }

        return dark;
    }

    /// <summary>Roughly what getting to a tile is worth, in ticks of walking.</summary>
    // An estimate and never a plan. What a trip really costs is the search's answer, and a
    // caller weighing forty candidates has no business drawing forty routes; what is wanted
    // is only which of two is dearer, and rock against open air decides that.
    //
    // Along the straight line, which no body walks. A cave the line passes through counts
    // as open and a wall counts as solid, and that is the whole of the fidelity needed to
    // stop a rich seam behind forty tiles of stone from beating a cave mouth next door.
    //
    // Never zero, because this is a divisor.
    public static float Reaching(ITerrain terrain, Point from, Point at)
    {
        int across = at.X - from.X;
        int down = at.Y - from.Y;
        int steps = System.Math.Max(System.Math.Abs(across), System.Math.Abs(down));
        if (steps == 0)
        {
            return 1f;
        }

        int rock = 0;
        for (int step = 1; step <= steps; step++)
        {
            int x = from.X + (across * step / steps);
            int y = from.Y + (down * step / steps);
            if (terrain.KindAt(x, y) is TileKind.Solid or TileKind.Slab)
            {
                rock++;
            }
        }

        return steps + (Rock * rock);
    }
}
