using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.Do;

/// <summary>
/// Killing things: choosing what, closing on it, and waiting where they spawn.
/// </summary>
// Targeting decides which creature; this decides whether to be in a fight at all, how
// to get within reach of the weapon in hand, and where to wait for a drop's source.
internal sealed class Fighting(Pilot pilot, Exploring exploring, Belief belief,
    Character character)
{
    private readonly Pilot _pilot = pilot;

    private readonly Belief _belief = belief;

    private readonly Character _character = character;

    private readonly Exploring _exploring = exploring;

    /// <summary>Node budget for reaching something in a fight.</summary>
    internal const int Chase = 2000;

    /// <summary>Who to fight, and what it takes to change its mind.</summary>
    private readonly Targeting _targeting = new();

    /// <summary>What the run is currently trying to get hold of.</summary>
    private int _seeking;

    /// <summary>Which creature the agent is currently in a fight with.</summary>
    private int _fighting = -1;

    private double _waitingSince;

    private double _lastHunt;

    private double _now;

    /// <summary>What the run is after, so the reflex and a goal agree on the target.</summary>
    public int Seeking
    {
        get => _seeking;
        set => _seeking = value;
    }

    /// <summary>
    /// Fight whatever the aggro box has caught, whatever the agent was doing.
    /// </summary>
    // Runs before the objective, so an agent fetching wood does not walk through a
    // fight it never noticed.
    public bool Nearby(AgentState state, double now)
    {
        _now = now;
        // Told what the run is after, so a slime the progression needs outranks a
        // nearer zombie. Last tick's answer, since the objective runs after this.
        if (_targeting.Choose(_belief, _character.Movement.Center, _seeking) is not { } target)
        {
            return false;
        }

        string what = _seeking != 0 && Drops.Yields(target.netID, _seeking)
            ? $"fighting {target.TypeName} ({target.life}/{target.lifeMax})"
            : $"dealing with {target.TypeName} ({target.life}/{target.lifeMax})";

        // False when out of reach for the moment (a ledge above, or mid-hop): the
        // target stays and the objective's move lets the question be asked again.
        return Engage(state, target, what);
    }

    /// <summary>Where something that drops this is, if one is in sight.</summary>
    // The choice Fight would make, asked without committing to it, so the job board
    // can pick a kill over a dig only when there is something to kill. The floor under
    // it, not the thing itself: a slime in mid-air has no footing, and a search to it
    // burns the whole node budget once a tick while it bounces.
    public Point? Sighted(int itemID) =>
        _targeting.Choose(_belief, _character.Movement.Center, itemID) is { } target
            ? _belief.Under(
                new Point((int)(target.Center.X / 16f), (int)(target.Center.Y / 16f)))
            : null;

    /// <summary>Kill something for what it drops.</summary>
    public void Fight(AgentState state, int wantedItemID,
        Point at, double now)
    {
        _now = now;
        // Set here, where the drop is known, because the objective usually asks for a
        // product (a torch) and the drop (gel) is steps further down the chain.
        _seeking = wantedItemID;

        if (_targeting.Choose(_belief, _character.Movement.Center, wantedItemID) is not { } target)
        {
            Hunt(state, wantedItemID, at);
            return;
        }

        string what = Drops.Yields(target.netID, wantedItemID)
            ? $"fighting {target.TypeName} ({target.life}/{target.lifeMax})"
            : $"clearing {target.TypeName} to make room for spawns";

        if (Engage(state, target, what))
        {
            return;
        }

        // Genuinely cannot be got at: across a ravine, or up a cliff.
        state.Stuck = false;
        Hunt(state, wantedItemID, at);
    }

    /// <summary>Fight this one. False when it cannot be got at from here.</summary>
    private bool Engage(AgentState state, NPC target,
        string what)
    {
        Player player = _character.Player;

        // Hold it before measuring anything: reach is the held item's, and a pickaxe's
        // reach puts a sword short of the target.
        if (!_character.Hand.HoldWeapon())
        {
            state.Behaviour = "nothing to fight with";
            state.Stuck = true;
            return true;
        }

        Item weapon = player.inventory[player.selectedItem];
        float range = _character.Hand.StrikePixels;

        Point tile = new((int)(target.Center.X / 16f), (int)(target.Center.Y / 16f));
        state.Goal = tile;

        // Once swinging, keep swinging until it is properly out of reach, or a target
        // on the line makes the agent step, swing, step back.
        float engaged = _fighting == target.whoAmI ? range * Weapons.Hysteresis : range;
        bool near = Vector2.DistanceSquared(target.Center, _character.Movement.Center)
                    <= engaged * engaged;

        // Nothing is attacked through a wall, thrown or swung. A shot into rock is
        // obviously wasted; a swing is what left the agent grinding at a jellyfish on
        // the far side of a wall, in range and unable to touch it, because the arc is
        // measured in pixels and knows nothing about what fills them.
        Rectangle body = _character.Movement.Frame;
        bool sees = _belief.CanSee(new Vector2(body.X, body.Y), body.Width, body.Height,
            target.position, target.width, target.height);

        // A swing also needs the target in the arc, not merely close, because a hopping
        // target spends half the fight above it.
        bool able = near && sees
            && (_character.Hand.Throws || _character.Hand.Strikes(target));

        if (able)
        {
            _fighting = target.whoAmI;
            _character.Hand.AimAt(target.Center);
            _character.Hand.Use();
            state.Behaviour = _character.Hand.Blocked
                ? "holding the swing until the game aims where we do"
                : $"{what} with {weapon.Name}";
            return true;
        }

        // Close to the weapon's range rather than to the creature, or a bow gets used
        // as a club.
        _fighting = -1;
        Vector2 at = target.Center;

        // Somewhere the weapon can be used from, which means a clear line to the target
        // whatever is held: the search climbs over what is in the way rather than
        // walking up to the other side of it and swinging at rock.
        bool Ready(Point node) =>
            Body.InRangeFrom(node, at, range)
            && _belief.CanSee(Body.Stand(node), Body.PixelWidth, Body.PixelHeight,
                target.position, target.width, target.height);

        // A small budget: a fight is near by definition, and the goal shifts a tile
        // every frame so a failed search is never remembered. A creature on a ledge
        // overhead would otherwise cost the full budget every tick.
        if (_pilot.Advance(_belief, _character, state, tile, _now,
                arriveWithinTiles: 1, arrived: Ready, maxNodes: Chase))
        {
            // Says whether the target is wanted or merely in the way.
            state.Behaviour = $"{what}, closing to {range / 16f:0.#} tiles "
                        + $"with {weapon.Name}: {_pilot.Behaviour}";
            return true;
        }

        // Nowhere to shoot or swing from this instant is the normal case for a hopping
        // target, not a reason to wait: walk to the ground under it and ask again.
        state.Stuck = false;
        Point under = _belief.Under(tile);
        if (_pilot.Advance(_belief, _character, state, under, _now, arriveWithinTiles: 1,
                maxNodes: Chase))
        {
            state.Behaviour = $"{what}, closing with {weapon.Name}: " + _pilot.Behaviour;
            return true;
        }

        state.Stuck = false;
        return false;
    }

    private void Hunt(AgentState state, int wantedItemID,
        Point at)
    {
        string what = Lang.GetItemNameValue(wantedItemID);

        // Nothing known is not the same as here. SpawnZones reads the bestiary's depth
        // tags, and a jellyfish carries none (Ocean is a biome, not a depth), so an
        // empty answer must not fall through to wherever the agent is standing.
        if (SpawnZones.For(wantedItemID).Count == 0)
        {
            state.Behaviour = $"do not know where {Hunted(wantedItemID)}, for {what}, turns up";
            state.Stuck = true;
            return;
        }

        if (SpawnZones.Nearest(wantedItemID, at.Y, Main.dayTime) is not { } band)
        {
            state.Behaviour = $"nothing that drops {what} spawns "
                        + $"{(Main.dayTime ? "by day" : "at night")}";
            state.Stuck = true;
            return;
        }

        // Travelling to the band is not wandering, above or below alike.
        if (band != Layers.At(at.Y))
        {
            _exploring.GetToRow(state, Layers.EntryRow(band), at,
                $"{what} from the {band}", _now);
            return;
        }

        // Standing in the right place is hunting. Terraria spawns around the player, so
        // once in the band walking adds nothing but the risk of leaving it.
        if (_now - _lastHunt > 0.5)
        {
            _waitingSince = _now;
        }

        _lastHunt = _now;
        _pilot.Forget();
        state.Goal = null;
        // Named by the creature that has to turn up, not by what it is for.
        state.Behaviour = $"waiting in the {band} for {Hunted(wantedItemID)}: "
                    + $"{_now - _waitingSince:0}s";
    }

    /// <summary>What has to show up for this item to be had, in words.</summary>
    // Asked of the drop table, so nothing here names a slime. Names one and counts
    // the rest, since most things have several droppers.
    public static string Hunted(int itemID)
    {
        IReadOnlyList<int> sources = Drops.Droppers(itemID);
        if (sources.Count == 0)
        {
            return Lang.GetItemNameValue(itemID);
        }

        string first = Lang.GetNPCNameValue(sources[0]);
        return sources.Count == 1 ? first : $"{first} or {sources.Count - 1} others";
    }
}
