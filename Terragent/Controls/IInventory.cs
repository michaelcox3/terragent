using System.Collections.Generic;

namespace Terragent.Controls;

/// <summary>What is carried, what is worn, and what the bag can do with either.</summary>
// Everything here is named by item, never by slot. Slot numbers are Terraria's layout,
// ten of hotbar then forty of bag then three of armour then accessories, and a caller
// that knows them decides where things go; then two callers disagree. Which slot a
// thing ends up in is this file's business alone.
//
// No Terraria type appears here, which is what lets everything above it be checked with
// no game running.
internal interface IInventory
{
    /// <summary>The strongest pickaxe carried, as a power rather than an item.</summary>
    // A question about the bag. What a tile demands of a pickaxe is a fact about
    // Terraria and is asked elsewhere.
    int PickPower { get; }

    /// <summary>How many blocks may be built with, which is what prices a pillar.</summary>
    // Not everything placeable: ore and gems are wanted for what they craft into, and a
    // pillar out of a hole is not worth one.
    int Blocks { get; }

    /// <summary>The best pickaxe carried, as an item, or zero when none is.</summary>
    // What Hold is given. PickPower above is the same tool measured rather than named:
    // the search wants to know what it can break and the hand wants to know what to pick
    // up, and neither answer is the other one.
    int Pickaxe { get; }

    /// <summary>The best axe carried, as an item, or zero when none is.</summary>
    int Axe { get; }

    /// <summary>Whatever carried item hits hardest, or zero when nothing does.</summary>
    // A weapon by what it does, not by what it is called. A pickaxe swung at a slime is
    // a weapon, and early on it is the only one.
    int Weapon { get; }

    /// <summary>An ordinary block to build with, or zero when none may be spent.</summary>
    // The item that Blocks above counts. Which of several it names does not matter, since
    // a bridge is a bridge, and dirt is worth spending before wood.
    int Block { get; }

    /// <summary>How many of an item is carried, across every stack.</summary>
    int Carrying(int itemID);

    /// <summary>How many of an item may be spent on anything at all.</summary>
    // What is carried less what is held back. A pillar out of a hole must not burn the
    // last torch, and the only thing that can tell is the bag.
    int Spendable(int itemID);

    /// <summary>
    /// How many are carried of any of these, which is the useful question when a world
    /// offers one metal or the other.
    /// </summary>
    int Carrying(IReadOnlyList<int> any);

    /// <summary>Ticks one swing or one use of an item takes, or zero when it is nothing.</summary>
    // Any item, since the question is the same for a pickaxe, an axe and a sword, and
    // pricing a dig and pricing a fight would otherwise ask it two different ways. Read
    // as UseTime(Pickaxe).
    int UseTime(int itemID);

    /// <summary>Whether this is being worn, as armour or as an accessory.</summary>
    bool Equipped(int itemID);

    /// <summary>Whether the game counts a crafting station as within reach.</summary>
    // The game's own answer, since that is what decides what is craftable. Here beside
    // CanCraft rather than on the body: the two are the same question asked with and
    // without the materials, and reading them off one interface is what makes a missing
    // anvil tell itself apart from a missing bar.
    bool NearStation(int tileID);

    /// <summary>
    /// Whether one of these could be crafted right now: a recipe for it, its station in
    /// reach, and the materials in the bag.
    /// </summary>
    // The game's own availability list, which is the same list Craft spends from, so the
    // two cannot disagree about recipe groups or about what a stack of ore is worth.
    bool CanCraft(int itemID);

    /// <summary>Craft one, which is only valid where <see cref="CanCraft"/> says so.</summary>
    // One, not a stack: a craft is a tick of work, and how many are wanted is the
    // caller's to count.
    void Craft(int itemID);

    /// <summary>Hold this many of an item back from being spent.</summary>
    // A standing floor, set once when a supply comes into force, not a claim restated
    // every tick. Two callers reserving the same item leave the larger floor standing.
    void Reserve(int itemID, int count);

    /// <summary>Bring an item to the hotbar and hold it, so that using it uses that.</summary>
    // The one way anything moves between slots. A general move would need slot numbers
    // in the signature, and the only reason the agent ever shifts an item is to have it
    // in hand.
    void Hold(int itemID);

    /// <summary>Put on a carried piece of armour or an accessory.</summary>
    // What it displaces goes back to the bag, and which slot it belongs in comes from
    // the item, so nothing above this knows a helmet from a wing.
    void Equip(int itemID);

    /// <summary>Take a worn piece off, back into the bag.</summary>
    void Unequip(int itemID);

    /// <summary>Throw away this many of an item, which is how the bag stays open.</summary>
    // A count rather than the lot: dirt is worth carrying a stack of and worth nothing
    // beyond that, and the caller is the one that knows its floor.
    void Trash(int itemID, int count);
}
