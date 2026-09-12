using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Killing something for what it drops.</summary>
// It holds the one it chose by index and asks about that one every tick. Slots are reused
// the moment a creature dies, so a job that remembered a position would keep swinging at
// whatever spawned into its place.
//
// What it drops is not collected here. Killing a slime and walking onto the gel are two
// jobs, and joining them would mean a fight that is not finished until the loot is picked
// up, which is a fight that never ends when the loot falls in lava.
internal sealed class Fight(
    ICreatures creatures,
    IInventory bag,
    IHand hand,
    IJournal journal,
    IReadOnlyList<int> types) : IJob
{
    private readonly ICreatures _creatures = creatures;
    private readonly IInventory _bag = bag;
    private readonly IHand _hand = hand;

    /// <summary>Whether a swing has been thrown at it yet.</summary>
    // Once engaged, a little more reach counts as still engaged. Kept for the life of
    // the job, which ends when the thing dies or walks off, so the wider range never
    // outlives the fight it was granted for.
    private bool _swinging;

    /// <summary>How much further than plain reach still counts, once swinging.</summary>
    private const float Engaged = 1.35f;

    /// <summary>Footings the search may look at before giving up on a fight.</summary>
    private const int Patience = 4000;

    // The kinds rather than the one it is fighting. A job describes work and holds no
    // state, so what it is currently swinging at is the foreman's to know.
    public string Label => $"Fight {Names.Any(types)}";

    /// <summary>Two fights over the same kinds of creature are one piece of work.</summary>
    // By the label, which is made of the same thing the work is: the item, or the kinds.
    public bool Equals(IJob? other) => other is Fight same && same.Label == Label;

    public override bool Equals(object? other) => Equals(other as IJob);

    public override int GetHashCode() => System.HashCode.Combine(nameof(Fight), Label);

    /// <summary>Never finished, because there is always something else to kill.</summary>
    // Whether the one it chose is dead is Workable's question, and the objective decides
    // when enough have died by watching what they drop.
    public bool Done => false;

    /// <summary>Still worth being here while the thing is still alive.</summary>
    // Its own answer rather than the site, since the site is where the creature was and it
    // has moved since.
    /// <summary>Still worth being here while the thing it chose is alive.</summary>
    public bool Workable(ITarget target) =>
        target is CreatureTarget { } quarry && _creatures.At(quarry.Index) is not null;

    public Offer? Nearest(Point from)
    {
        if (_creatures.Nearest(from, types) is not { } creature)
        {
            return null;
        }

        // Written when it changes, so a fight that cannot be planned says why once
        // rather than sixty times a second.
        journal.Change("fighting", $"{Names.Creature(creature.Type)} at "
            + $"({Tile(creature.Middle).X}, {Tile(creature.Middle).Y}) with "
            + $"{Names.Item(_bag.Weapon)}, in reach from here "
            + $"{_hand.CanHitFrom(from, creature.Middle, _bag.Weapon)}");

        // Arrival is the hand's answer, not a radius. A creature three tiles away through
        // a wall is close and unhittable, and a radius says go, so the agent stands there
        // swinging at rock. This is the jellyfish the old mod punched for a minute.
        //
        // A short budget, because a fight that needs a long search is a fight to walk away
        // from rather than one to plan a tunnel for.
        // The creature is looked up every time rather than remembered. A slime crosses a
        // tile a second, and a predicate holding where it was walks the body to a spot it
        // could have hit a moment ago and stops there swinging at nothing.
        int index = creature.Index;
        return new Offer(
            new CreatureTarget(_creatures, index),
            new Destination(Tile(creature.Middle), Budget: Patience)
            {
                Arrived = footing => _creatures.At(index) is { } now
                    && _hand.CanHitFrom(footing, now.Middle, _bag.Weapon,
                        _swinging ? Engaged : 1f),
            });
    }

    public void Work(ITarget target)
    {
        if (target is not CreatureTarget quarry
            || _creatures.At(quarry.Index) is not { } creature)
        {
            return;
        }

        // Held every tick, since anything else raised in between leaves a torch in hand
        // when the swing lands.
        _bag.Hold(_bag.Weapon);

        // Aimed at the body rather than at its tile: eight pixels of rounding is the
        // difference between a swing landing and missing at the edge of reach, and it is
        // which way a shot goes.
        _hand.AimAt(creature.Middle);
        _hand.Use();
        _swinging = true;
    }

    private static Point Tile(Vector2 middle) =>
        new((int)(middle.X / 16f), (int)(middle.Y / 16f));
}
