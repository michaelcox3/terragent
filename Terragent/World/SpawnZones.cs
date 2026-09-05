using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;

namespace Terragent.World;

/// <summary>
/// Where the things that drop a given item actually spawn.
/// </summary>
internal static class SpawnZones
{
    private static readonly Dictionary<int, List<(Layer Layer, bool Day, bool Night)>>
        _cache = [];

    /// <summary>
    /// The nearest layer where something that drops this item spawns right now.
    /// </summary>
    public static Layer? Nearest(int itemID, int y, bool dayTime)
    {
        Layer? best = null;
        int nearest = int.MaxValue;

        foreach ((Layer layer, bool day, bool night) in For(itemID))
        {
            if (dayTime ? !day : !night)
            {
                continue;
            }

            (int top, int bottom) = Layers.Band(layer);
            int distance = y < top ? top - y : y > bottom ? y - bottom : 0;
            if (distance < nearest)
            {
                nearest = distance;
                best = layer;
            }
        }

        return best;
    }

    /// <summary>Every band something dropping this item spawns in, and when.</summary>
    public static IReadOnlyList<(Layer Layer, bool Day, bool Night)> For(int itemID)
    {
        if (_cache.TryGetValue(itemID, out List<(Layer, bool, bool)>? known))
        {
            return known;
        }

        Dictionary<Layer, (bool Day, bool Night)> found = [];
        foreach (int type in Drops.Droppers(itemID))
        {
            Read(type, found);
        }

        List<(Layer, bool, bool)> zones = [];
        foreach ((Layer layer, (bool day, bool night)) in found)
        {
            zones.Add((layer, day, night));
        }

        _cache[itemID] = zones;
        return zones;
    }

    /// <summary>
    /// Fold one creature's bestiary tags into the set of bands.
    /// </summary>
    private static void Read(int creatureID, Dictionary<Layer, (bool Day, bool Night)> into)
    {
        BestiaryEntry? entry;
        try
        {
            entry = Main.BestiaryDB.FindEntryByNPCID(creatureID);
        }
        catch (System.Exception)
        {
            return;
        }

        if (entry?.Info is null)
        {
            return;
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

            if (ReferenceEquals(tag, BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Surface))
            {
                bands.Add(Layer.Surface);
            }
            else if (ReferenceEquals(tag, BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Underground))
            {
                bands.Add(Layer.Underground);
            }
            else if (ReferenceEquals(tag, BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Caverns))
            {
                bands.Add(Layer.Cavern);
            }
            else if (ReferenceEquals(tag, BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.TheUnderworld))
            {
                bands.Add(Layer.Underworld);
            }
            else if (ReferenceEquals(tag, BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Times.DayTime))
            {
                day = true;
            }
            else if (ReferenceEquals(tag, BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Times.NightTime))
            {
                night = true;
            }
        }

        // No time tag means no restriction, which is most things.
        if (!day && !night)
        {
            day = night = true;
        }

        foreach (Layer band in bands)
        {
            (bool wasDay, bool wasNight) = into.TryGetValue(band, out (bool, bool) prior)
                ? prior
                : (false, false);
            into[band] = (wasDay || day, wasNight || night);
        }
    }
}
