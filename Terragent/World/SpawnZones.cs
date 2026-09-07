using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Bestiary;

namespace Terragent.World;

/// <summary>Where a kind of creature spawns, and when.</summary>
// Read out of the Bestiary, which is the game's own answer and stays true across versions.
// The alternative is a hand-written table of where slimes live, and Terraria's real rule is
// a thousand lines of conditions inside NPC.SpawnNPC that nothing can read.
//
// Layers only, not biomes. Depth is what a run can act on: it can dig down to the caverns,
// and it cannot decide to be in a jungle that its world put on the other side of the map.
internal static class SpawnZones
{
    private static readonly Dictionary<int, List<(Layer Layer, bool Day, bool Night)>> _bands = [];

    /// <summary>The nearest band any of these spawn in at this time of day.</summary>
    public static Layer? Nearest(IReadOnlyList<int> types, int y, bool day)
    {
        Layer? best = null;
        int nearest = int.MaxValue;

        foreach (int type in types)
        {
            foreach ((Layer layer, bool byDay, bool byNight) in For(type))
            {
                if (day ? !byDay : !byNight)
                {
                    continue;
                }

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

    /// <summary>Every band one creature spawns in, and whether by day or night.</summary>
    private static IReadOnlyList<(Layer Layer, bool Day, bool Night)> For(int type)
    {
        if (_bands.TryGetValue(type, out List<(Layer, bool, bool)>? known))
        {
            return known;
        }

        List<(Layer, bool, bool)> zones = [];
        _bands[type] = zones;

        BestiaryEntry? entry;
        try
        {
            entry = Main.BestiaryDB.FindEntryByNPCID(type);
        }
        catch (System.Exception)
        {
            return zones;
        }

        if (entry?.Info is null)
        {
            return zones;
        }

        List<Layer> bands = [];
        bool day = false;
        bool night = false;

        foreach (IBestiaryInfoElement element in entry.Info)
        {
            if (element is not SpawnConditionBestiaryInfoElement tag)
            {
                continue;
            }

            SpawnConditionBestiaryInfoElement[] biomes =
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Surface,
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Underground,
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Caverns,
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.TheUnderworld,
            ];

            for (int n = 0; n < biomes.Length; n++)
            {
                if (ReferenceEquals(tag, biomes[n]))
                {
                    bands.Add((Layer)(n + 1));
                }
            }

            day |= ReferenceEquals(tag,
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Times.DayTime);
            night |= ReferenceEquals(tag,
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Times.NightTime);
        }

        // No time tag means no restriction, which is most things.
        if (!day && !night)
        {
            day = night = true;
        }

        foreach (Layer band in bands)
        {
            zones.Add((band, day, night));
        }

        return zones;
    }
}
