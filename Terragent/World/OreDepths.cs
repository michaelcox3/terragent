using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.World;

/// <summary>One band worldgen scatters an ore through, and how thickly.</summary>
/// <param name="Top">First row, inclusive.</param>
/// <param name="Bottom">Last row, exclusive.</param>
/// <param name="Density">
/// Veins per tile of world area, straight from the generation pass. Only ever compared
/// against other entries, so the absolute value does not matter; what matters is that
/// iron below the rock layer is two and a half times thicker than iron above it.
/// </param>
internal readonly record struct OreBand(int Top, int Bottom, double Density);

/// <summary>
/// Where worldgen actually puts each ore.
/// </summary>
internal static class OreDepths
{
    private static int Surface => (int)Main.worldSurface;

    private static int Rock => (int)Main.rockLayer;

    private static int Bottom => Main.maxTilesY;

    /// <summary>The bands this ore tile is generated in, richest first.</summary>
    public static IReadOnlyList<OreBand> For(int tileID)
    {
        // Bands and densities follow the worldgen ore passes.
        List<OreBand> bands = tileID switch
        {
            TileID.Copper or TileID.Tin =>
            [
                new OreBand(Rock, Bottom, 0.0002),
                new OreBand(Surface, Rock, 0.00008),
                new OreBand(Surface - 40, Surface, 0.00006),
            ],

            TileID.Iron or TileID.Lead =>
            [
                new OreBand(Rock, Bottom, 0.0002),
                new OreBand(Surface, Rock, 0.00008),
                new OreBand(Surface - 40, Surface, 0.00003),
            ],

            TileID.Silver or TileID.Tungsten =>
            [
                new OreBand(Surface, Rock, 0.00015),
                new OreBand(Rock - 100, Bottom - 250, 0.000026),
            ],

            TileID.Gold or TileID.Platinum =>
            [
                new OreBand(Surface, Rock, 0.00012),
                new OreBand(Rock, Bottom - 250, 0.00012),
            ],

            // Not ores: stone and dirt are the layers themselves.
            TileID.Stone => [new OreBand(Surface, Bottom, 1.0)],
            TileID.Dirt => [new OreBand(0, Rock, 1.0)],

            // Pots go in caves anywhere from just under the surface to above the
            // underworld, and the pass does not thin with depth. One band, so an agent
            // short of glowsticks goes down.
            TileID.Pots => [new OreBand(Surface, Bottom - 200, 0.00025)],
            _ => [],
        };

        bands.Sort((a, b) => b.Density.CompareTo(a.Density));
        return bands;
    }

    /// <summary>Whether a row is inside any band this ore generates in.</summary>
    public static bool Holds(int tileID, int y)
    {
        foreach (OreBand band in For(tileID))
        {
            if (y >= band.Top && y < band.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The row worth travelling to, from where the agent currently is.
    /// </summary>
    public static int? BestRow(int tileID, int from)
    {
        IReadOnlyList<OreBand> bands = For(tileID);
        if (bands.Count == 0)
        {
            return null;
        }

        // Just inside the edge, not a fraction of the way in: a tenth of the stone band
        // is ninety rows of digging for something five below the agent's feet. The same
        // margin from inside the band as from outside, or a route crossing the band's
        // edge flips the prospecting row between "here" and "eight down".
        const int Margin = 8;
        OreBand richest = bands[0];
        int top = Math.Min(richest.Top + Margin, richest.Bottom - 1);
        int bottom = Math.Max(richest.Bottom - Margin, top);
        return Math.Clamp(from, top, bottom);
    }
}
