using Terraria;

namespace Terragent.World;

/// <summary>Where in the world a given row is, and where a band begins and ends.</summary>
// The boundaries are per world, not constants: a small world's caverns start higher than a
// large one's, and the game recomputes them at generation.
internal static class Layers
{
    public static Layer At(int y)
    {
        if (y > Main.UnderworldLayer)
        {
            return Layer.Underworld;
        }

        if (y > Main.rockLayer)
        {
            return Layer.Cavern;
        }

        if (y > Main.worldSurface)
        {
            return Layer.Underground;
        }

        return y > Main.worldSurface * 0.35 ? Layer.Surface : Layer.Space;
    }

    /// <summary>The rows a band covers, top and bottom inclusive.</summary>
    public static (int Top, int Bottom) Band(Layer layer) => layer switch
    {
        Layer.Space => (0, (int)(Main.worldSurface * 0.35)),
        Layer.Surface => ((int)(Main.worldSurface * 0.35) + 1, (int)Main.worldSurface),
        Layer.Underground => ((int)Main.worldSurface + 1, (int)Main.rockLayer),
        Layer.Cavern => ((int)Main.rockLayer + 1, Main.UnderworldLayer),
        _ => (Main.UnderworldLayer + 1, Main.maxTilesY - 1),
    };

    /// <summary>A row worth travelling to in order to look for something in this band.</summary>
    // The middle. Just inside the top is cheaper to reach and is where a run then spends
    // itself: arriving at the ceiling of a band leaves everything worth finding below, so
    // every answer afterwards is a downward one and the body cuts a staircase instead of
    // walking the band it came for.
    public static int EntryRow(Layer layer)
    {
        (int top, int bottom) = Band(layer);
        return (top + bottom) / 2;
    }
}
