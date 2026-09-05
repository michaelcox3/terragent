using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.Act;

/// <summary>
/// The fifty slots and the cursor: what is carried, what may be spent, and moving
/// items between them.
/// </summary>
// One of the three units of Act. Anything that moves an item between slots is the
// inventory's.
internal sealed class Inventory(Player player)
{
    private readonly Player _player = player;

    private readonly Item[] _slots = player.inventory;

    /// <summary>What the job in hand has claimed, by item.</summary>
    // Spoken for, not reserved: an upkeep's reserve is a standing floor from
    // upkeep.json, and this is one job's claim on one tick.
    private readonly Dictionary<int, int> _spoken = [];

    /// <summary>How many of an item is carried, across every stack.</summary>
    public int Carrying(int itemID)
    {
        int total = 0;
        foreach (Item held in _slots)
        {
            if (held.type == itemID)
            {
                total += held.stack;
            }
        }

        return total;
    }

    /// <summary>
    /// How many are held of any of these, which a world offering one metal or the
    /// other makes the more useful question.
    /// </summary>
    public int Carrying(IReadOnlyList<int> any)
    {
        int total = 0;
        foreach (Item held in _slots)
        {
            if (held.IsAir)
            {
                continue;
            }

            for (int n = 0; n < any.Count; n++)
            {
                if (held.type == any[n])
                {
                    total += held.stack;
                    break;
                }
            }
        }

        return total;
    }

    /// <summary>The strongest pickaxe carried, as a power rather than an item.</summary>
    // A question about the bag, not about tiles: what a tile demands of a pickaxe is a
    // fact about Terraria, what pickaxe is carried is not.
    public int PickPower
    {
        get
        {
            int power = 0;
            foreach (Item item in _slots)
            {
                if (item.pick > power)
                {
                    power = item.pick;
                }
            }

            return power;
        }
    }

    /// <summary>
    /// The slot holding whichever item scores highest, or -1 when none scores.
    /// </summary>
    // All fifty, not the ten on the hotbar: BringToHotbar fetches out of the bag.
    public int BestSlot(Func<Item, int> measure)
    {
        int best = -1;
        int bestScore = 0;
        for (int slot = 0; slot < _slots.Length && slot < 50; slot++)
        {
            int score = measure(_slots[slot]);
            if (score > bestScore)
            {
                best = slot;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Whichever carried item scores highest, or nothing if none scores.</summary>
    public Item? Best(Func<Item, int> measure)
    {
        int slot = BestSlot(measure);
        return slot < 0 ? null : _slots[slot];
    }

    /// <summary>Forget last tick's claims, before they are restated.</summary>
    public void FreeAll() => _spoken.Clear();

    /// <summary>Keep this many of an item back from being built with.</summary>
    public void Reserve(ItemQuantity spoken)
    {
        if (spoken.ItemID != ItemID.None && spoken.Count > 0)
        {
            _spoken[spoken.ItemID] = Math.Max(spoken.Count,
                _spoken.TryGetValue(spoken.ItemID, out int had) ? had : 0);
        }
    }

    /// <summary>How many of an item the job in hand has claimed.</summary>
    public int Reserved(int itemID) =>
        _spoken.TryGetValue(itemID, out int count) ? count : 0;

    /// <summary>Whether this may be built with: building stock, and not spoken for.</summary>
    // Spending the thing the job came for is undoing the job, not a building decision.
    public bool Spendable(Item item) =>
        BuildingStock(item) && Carrying(item.type) > Reserved(item.type);

    /// <summary>How many placeable blocks may be spent, for costing a pillar.</summary>
    public int SpendableBlocks
    {
        get
        {
            int total = 0;
            HashSet<int> counted = [];
            foreach (Item item in _slots)
            {
                if (Spendable(item) && counted.Add(item.type))
                {
                    total += Math.Max(0, Carrying(item.type) - Reserved(item.type));
                }
            }

            return total;
        }
    }

    /// <summary>Whether the agent is allowed to build with this item at all.</summary>
    // Ore is not building material. Nor are gems: the agent will want them, they take a
    // long time to find, and a pillar out of a hole is not worth one.
    private static bool BuildingStock(Item item)
    {
        if (item.createTile < TileID.Dirt || !item.consumable
            || !Main.tileSolid[item.createTile]
            || Main.tileFrameImportant[item.createTile])
        {
            return false;
        }

        return !TileID.Sets.Ore[item.createTile] && !KeptForCrafting.Contains(item.createTile);
    }

    private static readonly HashSet<int> KeptForCrafting =
    [
        TileID.Amethyst, TileID.Topaz, TileID.Sapphire, TileID.Emerald, TileID.Ruby,
        TileID.Diamond, TileID.AmberStoneBlock, TileID.Obsidian, TileID.Hellstone,
        TileID.Meteorite, TileID.LihzahrdBrick,
    ];

    /// <summary>
    /// Move a slot's item onto the hotbar if it is not there, and say which slot it ends
    /// up in.
    /// </summary>
    public int BringToHotbar(int slot)
    {
        if (slot < 10)
        {
            return slot;
        }

        int target = FreeHotbarSlot();
        (_slots[target], _slots[slot]) = (_slots[slot], _slots[target]);
        return target;
    }

    /// <summary>An empty hotbar slot, or the last one if the hotbar is full.</summary>
    private int FreeHotbarSlot()
    {
        for (int slot = 0; slot < 10; slot++)
        {
            if (_slots[slot].IsAir)
            {
                return slot;
            }
        }

        return 9;
    }

    /// <summary>
    /// Craft one of <paramref name="itemID"/>, or false if it is not craftable here.
    /// </summary>
    public bool CraftItem(int itemID)
    {
        Refresh();
        for (int i = 0; i < Main.numAvailableRecipes; i++)
        {
            Recipe recipe = Main.recipe[Main.availableRecipe[i]];
            if (recipe.createItem.type != itemID)
            {
                continue;
            }

            Main.CraftItem(recipe);
            TakeFromCursor();
            return true;
        }

        return false;
    }

    /// <summary>Whether the game counts this crafting station as within reach.</summary>
    // The game's own answer. Player.adjTile is what FindRecipes reads to decide what is
    // craftable, so anything else is a second opinion about a question already settled.
    public bool AtStation(int tileID) =>
        tileID > 0 && tileID < _player.adjTile.Length && _player.adjTile[tileID];

    /// <summary>Whether <paramref name="itemID"/> could be crafted right now.</summary>
    public bool CanCraft(int itemID)
    {
        Refresh();
        for (int i = 0; i < Main.numAvailableRecipes; i++)
        {
            if (Main.recipe[Main.availableRecipe[i]].createItem.type == itemID)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Move whatever crafting left on the cursor into the slots.
    /// </summary>
    private void TakeFromCursor()
    {
        if (Main.mouseItem.IsAir)
        {
            return;
        }

        Main.mouseItem = _player.GetItem(_player.whoAmI, Main.mouseItem,
            GetItemSettings.InventoryEntityToPlayerInventorySettings);
    }

    /// <summary>
    /// Recompute which stations are in reach, then which recipes are available.
    /// </summary>
    private void Refresh()
    {
        _player.AdjTiles();
        Recipe.FindRecipes();
    }
}
