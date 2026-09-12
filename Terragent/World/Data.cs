using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terragent.World;

/// <summary>
/// Reading the agent's tables out of the files that describe them.
/// </summary>
// The spine and the standings are data, not code to recompile to change a number. The
// cost is that a typo is a runtime failure, so every name is resolved here at load and
// a bad one throws with the name in it rather than quietly becoming zero.
internal static class Data
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Files already taken out of the mod, so each is read from it once.</summary>
    // A load once table rather than state: the contents cannot change while the game is
    // running, and what it saves is not time but a fault. tModLoader serves the files in a
    // .tmod from one stream, and reading a second one while the first is open throws.
    // Reading these happens inside building a character, which the game does once per save
    // on disk, so six saved characters meant six overlapping reads and the run died at the
    // menu before it had entered anything.
    private static readonly Dictionary<string, JsonElement> Read_ = [];

    /// <summary>Parse a file packed into the mod.</summary>
    public static JsonElement Read(Mod mod, string path)
    {
        if (Read_.TryGetValue(path, out JsonElement known))
        {
            return known;
        }

        byte[] bytes = mod.GetFileBytes(path)
            ?? throw new InvalidOperationException($"{path} is not in the mod");

        JsonElement root = JsonDocument.Parse(bytes, Lenient).RootElement.Clone();
        Read_[path] = root;
        return root;
    }

    /// <summary>An item id from its <see cref="ItemID"/> name.</summary>
    // The game's own dictionary, so the names in the file are the names in the wiki.
    public static int Item(string name) =>
        ItemID.Search.TryGetId(name, out int id)
            ? id
            : throw new InvalidOperationException($"no item called {name}");

    /// <summary>A tile id from its <see cref="TileID"/> name.</summary>
    public static int Tile(string name) =>
        TileID.Search.TryGetId(name, out int id)
            ? id
            : throw new InvalidOperationException($"no tile called {name}");

    /// <summary>An NPC id from its <see cref="NPCID"/> name.</summary>
    public static int Creature(string name) =>
        NPCID.Search.TryGetId(name, out int id)
            ? id
            : throw new InvalidOperationException($"no creature called {name}");

    public static int[] Items(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out JsonElement list))
        {
            return [];
        }

        List<int> items = [];
        foreach (JsonElement name in list.EnumerateArray())
        {
            items.Add(Item(name.GetString() ?? ""));
        }

        return [.. items];
    }

    /// <summary>Everything an objective wants, each filled by any of several items.</summary>
    // Two levels, because both questions have to be asked. All of the outer must be had;
    // any one of an inner will do. Gold armour is a helmet and a chainmail and a greaves,
    // and each of those is the gold one or the platinum one, which one level cannot say.
    //
    // The count sits on the inner, since a hundred wood and one helmet are both wants and
    // an objective can hold both.
    public static IReadOnlyList<NeededItem> Wanted(JsonElement parent, string field)
    {
        List<NeededItem> all = [];
        if (!parent.TryGetProperty(field, out JsonElement groups))
        {
            return all;
        }

        foreach (JsonElement group in groups.EnumerateArray())
        {
            int count = Number(group, "count", 1);
            List<(int ItemID, int Count)> options = [];
            foreach (int itemID in Items(group, "any"))
            {
                options.Add((itemID, count));
            }

            if (options.Count > 0)
            {
                all.Add(new NeededItem(options));
            }
        }

        return all;
    }

    /// <summary>NPC ids from their <see cref="NPCID"/> names.</summary>
    public static int[] Creatures(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out JsonElement list))
        {
            return [];
        }

        List<int> creatures = [];
        foreach (JsonElement name in list.EnumerateArray())
        {
            creatures.Add(Creature(name.GetString() ?? ""));
        }

        return [.. creatures];
    }

    public static string[] Strings(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out JsonElement list))
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement value in list.EnumerateArray())
        {
            values.Add(value.GetString() ?? "");
        }

        return [.. values];
    }

    public static string Text(JsonElement parent, string field, string fallback = "") =>
        parent.TryGetProperty(field, out JsonElement value)
            ? value.GetString() ?? fallback
            : fallback;

    public static int Number(JsonElement parent, string field, int fallback) =>
        parent.TryGetProperty(field, out JsonElement value)
            ? value.GetInt32()
            : fallback;

    public static float Decimal(JsonElement parent, string field, float fallback) =>
        parent.TryGetProperty(field, out JsonElement value)
            ? (float)value.GetDouble()
            : fallback;

    public static TEnum Choice<TEnum>(JsonElement parent, string field, TEnum fallback)
        where TEnum : struct =>
        parent.TryGetProperty(field, out JsonElement value)
        && Enum.TryParse(value.GetString(), out TEnum parsed)
            ? parsed
            : fallback;

    /// <summary>One of an enum's names, for a field that holds a list of them.</summary>
    public static TEnum Choice<TEnum>(string name, TEnum fallback)
        where TEnum : struct =>
        Enum.TryParse(name, out TEnum parsed) ? parsed : fallback;
}
