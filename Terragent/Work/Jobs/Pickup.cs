using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Walking onto something lying on the ground.</summary>
// The only job whose work is arriving. Terraria picks an item up when the body overlaps
// its box, so there is nothing to press and Work does nothing at all.
//
// Which makes the arrival test the whole job. Being near the tile is not enough: an item
// that has rolled against a wall sits most of a tile from where it looks, and the pilot
// has to be told to keep going until the boxes actually meet.
internal sealed class Pickup(
    IInventory bag,
    IDrops drops,
    int itemID,
    int count) : IJob
{
    private readonly IInventory _bag = bag;
    private readonly IDrops _drops = drops;

    public string Label => $"Pick up {Names.Item(itemID)}";

    /// <summary>Two pickups of the same thing are one piece of work.</summary>
    // By the label, which is made of the same thing the work is: the item, or the kinds.
    public bool Equals(IJob? other) => other is Pickup same && same.Label == Label;

    public override bool Equals(object? other) => Equals(other as IJob);

    public override int GetHashCode() => System.HashCode.Combine(nameof(Pickup), Label);

    public bool Done => _bag.Carrying(itemID) >= count;

    /// <summary>Worth being here while the thing it came for is still lying there.</summary>
    // A pickup job with nothing to pick up is over, and Done cannot say so: it counts what
    // is carried against the objective's target, and the few logs off one tree never add up
    // to a hundred.
    public bool Workable(ITarget target) =>
        target is DropTarget { } lying && _drops.At(lying.Index) is not null;

    public Offer? Nearest(Point from)
    {
        if (_drops.Nearest(from, [itemID]) is not { } drop)
        {
            return null;
        }

        // The box is looked up every time rather than remembered. An item slides after it
        // falls, and a remembered rectangle says arrived at a patch of ground the thing has
        // rolled off, or says arrived for ever at one it was taken from.
        int index = drop.Index;
        return new Offer(
            new DropTarget(_drops, index),
            new Destination(new Point(drop.Box.Center.X / 16, drop.Box.Center.Y / 16))
            {
                Arrived = footing =>
                    _drops.At(index) is { } now && Hitbox.Touches(footing, now.Box),
            });
    }

    // Nothing to do on arrival. The game hands the item over for standing there.
    public void Work(ITarget target)
    {
    }
}
