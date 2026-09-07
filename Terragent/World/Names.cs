using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.World;

/// <summary>What things are called, for anything a person reads.</summary>
// The game's own names, so a panel says Iron Ore rather than 11. Only the display and the
// journal have any business here: nothing the agent decides should depend on a word.
internal static class Names
{
    public static string Item(int itemID) =>
        itemID == ItemID.None ? "nothing" : Lang.GetItemNameValue(itemID);

    public static string Creature(int npcID) => Lang.GetNPCNameValue(npcID);

    /// <summary>One of a list, said as a list when it is short.</summary>
    // Gel comes off two dozen things and naming them all fills the panel, so the first
    // stands for the rest.
    public static string Any(IReadOnlyList<int> npcIDs) => npcIDs.Count switch
    {
        0 => "anything",
        1 => Creature(npcIDs[0]),
        _ => $"{Creature(npcIDs[0])} or {npcIDs.Count - 1} others",
    };
}
