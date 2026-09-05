using System.Collections.Generic;
using Terraria;

namespace Terragent.World;

/// <summary>
/// One thing the run needs, and what it is made of in turn, down to what nothing makes.
/// </summary>
/// <param name="ItemID">What has to be had. A leaf has this and little else.</param>
/// <param name="PerCraft">
/// How many of it the craft above wants, each time that craft is run.
/// </param>
/// <param name="StationTileID">
/// When this need <i>is</i> a crafting station, the tile it puts down; zero when it is
/// an ordinary material.
/// </param>
/// <param name="Recipe">The recipe that makes it, or null when nothing does.</param>
/// <param name="Needs">The stations and materials that recipe calls for.</param>
/// <param name="Instead">
/// Other items that would satisfy this same ingredient, when a recipe group says so.
/// </param>
// A node has to be able to be a leaf: wood is chopped and gel is dropped, nothing crafts
// either, and they are the whole output of the walk. So ItemID stands alone and Recipe
// is optional.
//
// PerCraft and StationTileID describe the edge from the parent, not the item: iron ore
// is three per iron bar and a different number elsewhere. Amounts stay per craft so the
// tree exists without the world; Requirements scales them against what is carried.
//
// StationTileID is a tile id, not the station. A work bench is an ordinary node in
// Needs with its own recipe; this field only says which requiredTile the node satisfies,
// and it is the tile because whoever handles it asks the tile whether one is nearby.
//
// Instead is the only "or". Everything in Needs must be had; anything in Instead does
// in place of this node and its subtree. As one list, a torch wanted all eight woods.
// The alternatives are whole nodes with their own recipes; choosing is Requirements' job.
internal sealed record Need(int ItemID, int PerCraft, int StationTileID, Recipe? Recipe,
    IReadOnlyList<Need> Needs, IReadOnlyList<Need> Instead)
{
    /// <summary>This node and everything Terraria would take in its place.</summary>
    public IEnumerable<Need> Choices
    {
        get
        {
            yield return this;
            foreach (Need other in Instead)
            {
                yield return other;
            }
        }
    }

    /// <summary>How many one run of this node's own recipe produces.</summary>
    // A torch recipe makes three, so wanting five torches is two crafts and two gels,
    // not five.
    public int Yields => Recipe is null ? 1 : System.Math.Max(1, Recipe.createItem.stack);

    /// <summary>Nothing makes this, so it is dug, chopped, killed for or found.</summary>
    public bool Raw => Recipe is null;
}
