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

    /// <summary>Parse a file packed into the mod.</summary>
    public static JsonElement Read(Mod mod, string path)
    {
        byte[] bytes = mod.GetFileBytes(path)
            ?? throw new InvalidOperationException($"{path} is not in the mod");

        return JsonDocument.Parse(bytes, Lenient).RootElement.Clone();
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
}
