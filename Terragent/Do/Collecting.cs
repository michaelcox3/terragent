using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.Do;

/// <summary>Picking up what is lying on the ground, or is about to be.</summary>
// Its own class because its state is its own: which drop is being fetched, and whether
// the body has already been reported beside it rather than on it.
internal sealed class Collecting(Pilot pilot, Belief belief, Character character)
{
    private readonly Pilot _pilot = pilot;
    private readonly Belief _belief = belief;
    private readonly Character _character = character;

    /// <summary>The drop the last stretch is being walked toward, so it is said once.</summary>
    private int _closing = -1;

    /// <summary>The dropped item being fetched, or -1 when none is.</summary>
    private int _collecting = -1;

    /// <summary>
    /// Walk to a wanted item lying on the ground. True when that is what to do.
    /// </summary>
    internal bool Collect(AgentState state, int itemID, double now)
    {
        // Stay on the one already being fetched. A felled tree drops eight at once and
        // whichever is nearest changes as they scatter, so choosing afresh is a full
        // search every tick while the character walks between two and gets neither.
        Item? nearest = Nearest(itemID, _collecting);

        if (nearest is null)
        {
            _collecting = -1;
            return false;
        }

        _collecting = nearest.whoAmI;


        // Already touching it. Terraria does the rest on its own, this frame or the
        // next, so there is nothing to press and nothing to wait for.
        if (_character.Movement.Frame.Intersects(nearest.Hitbox))
        {
            _collecting = -1;
            state.Behaviour = $"picking up {nearest.stack}x {nearest.Name}";
            return true;
        }

        // Where it will be by the time the agent is there, rather than where it is: a
        // drop spends its first second in the air, and the agent goes to where it will
        // land and waits.
        Rectangle box = _belief.Landing(nearest.Hitbox);
        Point where = Tile(box.Center);

        // Terraria's own pickup test, a body overlapping the box, asked of a footing.
        // Asked of the tile the item's centre is in, a footing whose floor row holds the
        // item counts, and the agent stands over a one-wide hole reporting it arrived.
        if (!_pilot.Advance(_belief, _character, state, where, now,
                arriveWithinTiles: 0,
                arrived: node => Body.Touches(node, box)))
        {
            // Visible but unreachable: across a chasm, or fallen into a pit.
            return false;
        }

        state.Goal = where;
        state.Behaviour = $"collecting {nearest.stack}x {nearest.Name} at ({where.X}, {where.Y})";

        // The route has arrived and the item is still not in the body. Arriving is
        // measured in tiles and collecting in pixels, and a footing is two columns wide
        // while the body is twenty pixels inside them, so the two disagree by less than
        // a tile and the pilot has nothing left to press. The last stretch is therefore
        // walked in pixels, toward the item itself.
        if (_pilot.Next is null)
        {
            _character.Movement.Toward(nearest.Center.X);

            if (_closing != nearest.whoAmI)
            {
                _closing = nearest.whoAmI;
                Log.Event("closing", $"{nearest.stack}x {nearest.Name} beside the body, not in it",
                    ("body", _character.Movement.Frame.ToString()),
                    ("item", nearest.Hitbox.ToString()),
                    ("apart", nearest.Center.X - _character.Movement.Center.X));
            }
        }

        return true;
    }

    /// <summary>
    /// The nearest visible drop of this item, or the one already being fetched.
    /// </summary>
    /// <param name="prefer">
    /// A drop to keep hold of whatever else is nearer, or -1 to take the nearest.
    /// </param>
    // The preference is the whole point: without it the goal moves every tick as the
    // drops scatter, and a moved goal is a fresh search.
    private Item? Nearest(int itemID, int prefer)
    {
        Item? nearest = null;
        float best = float.MaxValue;
        foreach (Item drop in _belief.VisibleDrops(itemID))
        {
            if (drop.whoAmI == prefer)
            {
                return drop;
            }

            float distance = Vector2.DistanceSquared(drop.Center, _character.Movement.Center);
            if (distance < best)
            {
                best = distance;
                nearest = drop;
            }
        }

        return nearest;
    }

    /// <summary>The box the nearest of this item is lying in, or will be, if any is in sight.</summary>
    // Asked of the box, never of the speed. A fresh drop is tossed upward and is still
    // for a tick at the top of the toss, so reading "still" as "settled" sends the
    // search after a tile in the canopy, which has no footing. Landing returns a resting
    // box unchanged.
    //
    // The box, not its tile: arriving at a drop is the body overlapping it, while a tile
    // asks the board to stand on the row above the footing, which is never true.
    // The one being fetched, not whichever is nearest this tick. The board asks where
    // this job is, and answering with a different drop each tick moves the site under
    // the selection while eight from one tree are still scattering.
    internal Rectangle? Fallen(int itemID) =>
        Nearest(itemID, _collecting) is { } nearest ? _belief.Landing(nearest.Hitbox) : null;

    private static Point Tile(Point pixel) => new(pixel.X / 16, pixel.Y / 16);
}
