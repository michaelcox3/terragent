using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;

namespace Terragent.World;

/// <summary>What depth a kind of tile is found at.</summary>
// The other half of what a run needs to go looking. Sites answers where something already
// seen is; this answers where to go when none has been seen at all, which on a fresh world
// is every ore it will ever want.
//
// Exploring alone cannot get there. A frontier is ground with something unseen to one side
// of it, so the edge of a revealed strip runs sideways and walking it reveals more of the
// same row: a run after iron paced the surface all afternoon, because everything below was
// unknown and unknown below is never a frontier.
//
// Written down rather than read from the game, unlike the Bestiary that answers the same
// question for creatures. Where ore generates lives in worldgen as a thousand lines of
// placement passes, and nothing exposes it.
internal static class TileZones
{
    private static readonly Dictionary<int, List<Layer>> Found = [];

    /// <summary>Read the table out of the mod, once, at load.</summary>
    public static void Load(Mod mod)
    {
        Found.Clear();

        JsonElement root = Data.Read(mod, "Data/mining.json");
        foreach (JsonElement entry in root.GetProperty("zones").EnumerateArray())
        {
            List<Layer> bands = [];
            foreach (string name in Data.Strings(entry, "layers"))
            {
                bands.Add(Data.Choice(name, Layer.Cavern));
            }

            Found[Data.Tile(Data.Text(entry, "tile"))] = bands;
        }
    }

    /// <summary>The nearest band any of these is found in, or null when none is written down.</summary>
    // Nearest by rows, so a run in the caverns after iron stays where it is rather than
    // climbing back to the underground for the shallower half of the same vein.
    //
    // Null for wood, for a tree, for anything the surface already holds. Those want the
    // ordinary frontier walk, and answering with a band would send a run underground to
    // look for a forest.
    public static Layer? Nearest(IReadOnlyList<int> tileIDs, int y)
    {
        Layer? best = null;
        int nearest = int.MaxValue;

        foreach (int tileID in tileIDs)
        {
            if (!Found.TryGetValue(tileID, out List<Layer>? bands))
            {
                continue;
            }

            foreach (Layer layer in bands)
            {
                (int top, int bottom) = Layers.Band(layer);
                int away = y < top ? top - y : y > bottom ? y - bottom : 0;
                if (away < nearest)
                {
                    nearest = away;
                    best = layer;
                }
            }
        }

        return best;
    }
}
