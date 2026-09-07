using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.Controls;

/// <summary>The fifty slots, the armour and the cursor.</summary>
// Every question the run asks of what it carries, answered in one place, by item rather
// than by slot. Which slot anything ends up in is settled here and nowhere else.
internal sealed class Inventory(Player player) : IInventory
{
    private readonly Player _player = player;

    private readonly Item[] _slots = player.inventory;

    /// <summary>Standing floors, by item: how many are never to be spent.</summary>
    private readonly Dictionary<int, int> _floors = [];

    /// <summary>What to report for a tool that has no use time of its own.</summary>
    private const int SwingTicks = 15;

    /// <summary>Accessory slots a character has before anything grants more.</summary>
    private const int BaseAccessories = 5;

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

    public int Blocks
    {
        get
        {
            int total = 0;
            HashSet<int> counted = [];
            foreach (Item item in _slots)
            {
                if (Buildable(item) && counted.Add(item.type))
                {
                    total += Spendable(item.type);
                }
            }

            return total;
        }
    }

    public int Pickaxe => Best(item => item.pick);

    public int Axe => Best(item => item.axe);

    public int Weapon => Best(Hurts);

    // By stack, so the biggest pile goes first and a bridge is not planned on the last
    // four blocks of something.
    public int Block => Best(item => Buildable(item) ? Spendable(item.type) : 0);

    public int Carrying(int itemID)
    {
        int total = 0;
        foreach (Item item in _slots)
        {
            if (item.type == itemID)
            {
                total += item.stack;
            }
        }

        return total;
    }

    public int Spendable(int itemID) =>
        Math.Max(0, Carrying(itemID) - (_floors.TryGetValue(itemID, out int floor) ? floor : 0));

    public void Reserve(int itemID, int count) =>
        _floors[itemID] = Math.Max(count, _floors.TryGetValue(itemID, out int had) ? had : 0);

    public int Carrying(IReadOnlyList<int> any)
    {
        int total = 0;
        foreach (Item item in _slots)
        {
            if (!item.IsAir && Names(any, item.type))
            {
                total += item.stack;
            }
        }

        return total;
    }

    public int UseTime(int itemID)
    {
        if (itemID == ItemID.None)
        {
            return 0;
        }

        foreach (Item item in _slots)
        {
            if (item.type == itemID)
            {
                return item.useTime > 0 ? item.useTime : SwingTicks;
            }
        }

        return 0;
    }

    public bool Equipped(int itemID)
    {
        if (itemID == ItemID.None)
        {
            return false;
        }

        foreach (Item worn in _player.armor)
        {
            if (worn.type == itemID)
            {
                return true;
            }
        }

        return false;
    }

    // Player.adjTile is what FindRecipes reads to decide what is craftable, so anything
    // else would be a second opinion about a question already settled.
    public bool NearStation(int tileID) =>
        tileID > 0 && tileID < _player.adjTile.Length && _player.adjTile[tileID];

    public bool CanCraft(int itemID)
    {
        Refresh();
        for (int n = 0; n < Main.numAvailableRecipes; n++)
        {
            if (Main.recipe[Main.availableRecipe[n]].createItem.type == itemID)
            {
                return true;
            }
        }

        return false;
    }

    public void Craft(int itemID)
    {
        Refresh();
        for (int n = 0; n < Main.numAvailableRecipes; n++)
        {
            Recipe recipe = Main.recipe[Main.availableRecipe[n]];
            if (recipe.createItem.type != itemID)
            {
                continue;
            }

            Main.CraftItem(recipe);
            TakeFromCursor();
            return;
        }
    }

    public void Hold(int itemID)
    {
        int slot = SlotOf(itemID);
        if (slot >= 0)
        {
            _player.selectedItem = ToHotbar(slot);
        }
    }

    public void Equip(int itemID)
    {
        int slot = SlotOf(itemID);
        if (slot < 0)
        {
            return;
        }

        int worn = WornSlotFor(_slots[slot]);
        if (worn >= 0)
        {
            // A swap, so whatever comes off lands in the slot the new piece left.
            (_player.armor[worn], _slots[slot]) = (_slots[slot], _player.armor[worn]);
        }
    }

    public void Unequip(int itemID)
    {
        if (itemID == ItemID.None)
        {
            return;
        }

        for (int worn = 0; worn < _player.armor.Length; worn++)
        {
            if (_player.armor[worn].type != itemID)
            {
                continue;
            }

            int free = FreeSlot();
            if (free >= 0)
            {
                (_slots[free], _player.armor[worn]) = (_player.armor[worn], _slots[free]);
            }

            return;
        }
    }

    public void Trash(int itemID, int count)
    {
        int left = count;
        foreach (Item item in _slots)
        {
            if (left <= 0)
            {
                return;
            }

            if (item.IsAir || item.type != itemID)
            {
                continue;
            }

            int taken = Math.Min(left, item.stack);
            item.stack -= taken;
            left -= taken;
            if (item.stack <= 0)
            {
                item.TurnToAir();
            }
        }
    }

    /// <summary>What an item is worth as a weapon, or zero when it is not one.</summary>
    // Ammo is excluded: a Wooden Arrow does five damage and a Wooden Bow four, so picking
    // by damage alone holds the arrows and fires nothing. A weapon that takes ammo scores
    // nothing while none is carried.
    private int Hurts(Item item) =>
        item.damage > 0 && item.pick == 0 && item.axe == 0 && item.hammer == 0
        && item.ammo == AmmoID.None
        && item.useStyle != ItemUseStyleID.None
        && (item.useAmmo == AmmoID.None || Loaded(item.useAmmo))
            ? item.damage
            : 0;

    private bool Loaded(int ammo)
    {
        foreach (Item item in _slots)
        {
            if (item.ammo == ammo && item.stack > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the agent may build with this at all.</summary>
    // Ore is not building material. Nor are gems: the agent will want them, they take a
    // long time to find, and a pillar out of a hole is not worth one.
    private static bool Buildable(Item item)
    {
        if (item.createTile < TileID.Dirt || !item.consumable
            || !Main.tileSolid[item.createTile]
            || Main.tileFrameImportant[item.createTile])
        {
            return false;
        }

        return !TileID.Sets.Ore[item.createTile] && !Kept.Contains(item.createTile);
    }

    /// <summary>Placeable things that are worth more crafted than built with.</summary>
    private static readonly HashSet<int> Kept =
    [
        TileID.Amethyst, TileID.Topaz, TileID.Sapphire, TileID.Emerald, TileID.Ruby,
        TileID.Diamond, TileID.AmberStoneBlock, TileID.Obsidian, TileID.Hellstone,
        TileID.Meteorite, TileID.LihzahrdBrick,
    ];

    /// <summary>Whichever carried item scores highest, or nothing when none scores.</summary>
    private int Best(Func<Item, int> measure)
    {
        int best = ItemID.None;
        int score = 0;
        foreach (Item item in _slots)
        {
            int worth = measure(item);
            if (worth > score)
            {
                best = item.type;
                score = worth;
            }
        }

        return best;
    }

    private static bool Names(IReadOnlyList<int> any, int itemID)
    {
        for (int n = 0; n < any.Count; n++)
        {
            if (any[n] == itemID)
            {
                return true;
            }
        }

        return false;
    }

    private int SlotOf(int itemID)
    {
        if (itemID == ItemID.None)
        {
            return -1;
        }

        for (int slot = 0; slot < _slots.Length; slot++)
        {
            if (_slots[slot].type == itemID)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>Which worn slot a piece belongs in, or minus one when it is not worn.</summary>
    // Head, body and legs are one slot each. An accessory takes the first empty one, and
    // the first of them when they are all full, which is what a player does.
    private int WornSlotFor(Item item)
    {
        if (item.headSlot > 0)
        {
            return 0;
        }

        if (item.bodySlot > 0)
        {
            return 1;
        }

        if (item.legSlot > 0)
        {
            return 2;
        }

        if (!item.accessory)
        {
            return -1;
        }

        for (int slot = 3; slot < 3 + Accessories; slot++)
        {
            if (_player.armor[slot].IsAir)
            {
                return slot;
            }
        }

        return 3;
    }

    /// <summary>Accessory slots this character actually has.</summary>
    // Five as standard, with more for a Demon Heart and for master mode. Asked rather
    // than assumed, or a run wears nothing in the slots it paid for.
    private int Accessories => BaseAccessories + _player.extraAccessorySlots;

    /// <summary>Move a slot onto the hotbar, and say where it ended up.</summary>
    // All fifty are searched, not the ten on the hotbar, since holding something is how it
    // gets fetched out of the bag in the first place.
    private int ToHotbar(int slot)
    {
        if (slot < 10)
        {
            return slot;
        }

        int target = FreeHotbarSlot();
        (_slots[target], _slots[slot]) = (_slots[slot], _slots[target]);
        return target;
    }

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

    private int FreeSlot()
    {
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            if (_slots[slot].IsAir)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>Move whatever crafting left on the cursor back into the slots.</summary>
    private void TakeFromCursor()
    {
        if (Main.mouseItem.IsAir)
        {
            return;
        }

        Main.mouseItem = _player.GetItem(_player.whoAmI, Main.mouseItem,
            GetItemSettings.InventoryEntityToPlayerInventorySettings);
    }

    /// <summary>Recompute which stations are in reach, then which recipes are available.</summary>
    private void Refresh()
    {
        _player.AdjTiles();
        Recipe.FindRecipes();
    }
}
